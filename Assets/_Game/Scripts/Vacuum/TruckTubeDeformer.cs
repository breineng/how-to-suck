using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    // Owned by one VacuumView. Create after its baseline wrapper poses are restored;
    // call Restore on disable and Dispose on destruction/replacement of the truck view.
    public sealed class TruckTubeDeformer : IDisposable
    {
        private sealed class Part
        {
            public MeshFilter Filter;
            public Mesh Source, Runtime;
            public Matrix4x4 LocalToRoot, RootToLocal;
            public Vector3[] LocalVertices, RootVertices, WorkingVertices, Normals;
            public Bounds Bounds;
        }

        private readonly Transform root;
        private readonly TruckTubeProfile profile;
        private readonly Part[] parts;
        private bool disposed, deformed;
        public int MeshCount => parts.Length;
        public bool IsDeformed => deformed;

        public TruckTubeDeformer(Transform truckRoot, IReadOnlyList<MeshFilter> hoseMeshes,
            TruckTubeProfile deformationProfile)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("Truck tube clones are runtime-only. Do not save them into assets/prefabs.");
            if (truckRoot == null) throw new ArgumentNullException(nameof(truckRoot));
            if (hoseMeshes == null || hoseMeshes.Count == 0) throw new ArgumentException("Select the seven authored hose meshes.", nameof(hoseMeshes));
            root = truckRoot;
            profile = deformationProfile ?? throw new ArgumentNullException(nameof(deformationProfile));
            parts = new Part[hoseMeshes.Count];
            var unique = new HashSet<MeshFilter>();
            try
            {
                // Validate/capture every source before changing any renderer binding.
                for (int i = 0; i < parts.Length; i++)
                {
                    var filter = hoseMeshes[i];
                    if (filter == null || !filter.transform.IsChildOf(root) || !unique.Add(filter))
                        throw new ArgumentException("Hose mesh references must be unique descendants of the truck root.");
                    var source = filter.sharedMesh;
                    if (source == null || !source.isReadable)
                        throw new InvalidOperationException("Enable Read/Write on the truck ModelImporter before creating tube clones: " + filter.name);
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null || renderer.isPartOfStaticBatch)
                        throw new InvalidOperationException("Tube meshes need unbatched MeshRenderers; keep deforming sleeves out of static batching: " + filter.name);
                    Matrix4x4 localToRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    if (!Finite(localToRoot) || Mathf.Abs(localToRoot.determinant) < 1e-12f)
                        throw new InvalidOperationException("Tube transform must be finite and invertible: " + filter.name);
                    Vector3[] vertices = source.vertices;
                    if (vertices.Length == 0) throw new InvalidOperationException("Tube source has no vertices: " + filter.name);
                    var rootVertices = new Vector3[vertices.Length];
                    for (int j = 0; j < vertices.Length; j++)
                    {
                        rootVertices[j] = localToRoot.MultiplyPoint3x4(vertices[j]);
                        if (!Finite(rootVertices[j]) || rootVertices[j].z < profile.FrontZ - .001f ||
                            rootVertices[j].z > profile.RearZ + .001f)
                            throw new InvalidOperationException("Source vertices do not fit the configured straight tube span. Verify imported root axes/bounds: " + filter.name);
                    }
                    parts[i] = new Part { Filter = filter, Source = source, LocalToRoot = localToRoot,
                        RootToLocal = localToRoot.inverse, LocalVertices = vertices, RootVertices = rootVertices,
                        WorkingVertices = new Vector3[vertices.Length], Normals = source.normals, Bounds = source.bounds };
                }
                // Clone copies all submeshes, indices, UVs, tangents/colors and other authored channels.
                // Never use MeshFilter.mesh: its implicit clone would hide ownership.
                foreach (var part in parts)
                {
                    part.Runtime = UnityEngine.Object.Instantiate(part.Source);
                    part.Runtime.name = part.Source.name + " [TruckTube runtime]";
                    part.Runtime.hideFlags = HideFlags.DontSave;
                    part.Runtime.MarkDynamic();
                }
                foreach (var part in parts) part.Filter.sharedMesh = part.Runtime;
            }
            catch { Dispose(); throw; }
        }

        public void Apply(float phase, float mouthScale, float pulseSize)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TruckTubeDeformer));
            // Validate arguments even if a malformed caller supplied only boundary vertices.
            profile.ScaleAt((profile.FrontZ + profile.RearZ) * .5f, phase, mouthScale, pulseSize);
            ValidateBindings();
            if (phase >= 1 && mouthScale == 1) { Restore(); return; }
            deformed = true;
            try
            {
                foreach (var part in parts)
                {
                    for (int i = 0; i < part.RootVertices.Length; i++)
                    {
                        Vector3 p = part.RootVertices[i]; // Always baseline; never accumulate deformation.
                        profile.DeformRootXY(p.x, p.y, p.z, phase, mouthScale, pulseSize, out float x, out float y);
                        part.WorkingVertices[i] = part.RootToLocal.MultiplyPoint3x4(new Vector3(x, y, p.z));
                    }
                    part.Runtime.vertices = part.WorkingVertices;
                    part.Runtime.RecalculateNormals();
                    part.Runtime.RecalculateBounds();
                }
                deformed = true;
            }
            catch { Restore(); throw; }
        }

        public void Restore()
        {
            if (disposed || !deformed) return;
            foreach (var part in parts)
            {
                if (part == null || part.Runtime == null) continue;
                part.Runtime.vertices = part.LocalVertices;
                part.Runtime.normals = part.Normals;
                part.Runtime.bounds = part.Bounds;
            }
            deformed = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var part in parts)
            {
                if (part == null || part.Runtime == null) continue;
                // Do not overwrite a new model assigned by another owner during a tier/reload change.
                if (part.Filter != null && part.Filter.sharedMesh == part.Runtime)
                    part.Filter.sharedMesh = part.Source;
                if (Application.isPlaying) UnityEngine.Object.Destroy(part.Runtime);
                else UnityEngine.Object.DestroyImmediate(part.Runtime); // Play-mode exit, only our runtime clone.
                part.Runtime = null;
            }
            deformed = false;
        }

        private void ValidateBindings()
        {
            if (root == null) throw new InvalidOperationException("Truck root was destroyed; dispose its tube helper.");
            foreach (var part in parts)
            {
                if (part.Filter == null || part.Runtime == null || part.Filter.sharedMesh != part.Runtime)
                    throw new InvalidOperationException("Tube mesh binding changed. Dispose and rebuild for the new view.");
                Matrix4x4 current = root.worldToLocalMatrix * part.Filter.transform.localToWorldMatrix;
                if (!Finite(current) || !Approximately(current, part.LocalToRoot))
                    throw new InvalidOperationException("Tube transform changed after capture. Disable rigid hose wrapper scaling and rebuild only from baseline poses.");
            }
        }

        private static bool Approximately(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .0001f) return false; return true; }
        private static bool Finite(Matrix4x4 matrix)
        { for (int i = 0; i < 16; i++) if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false; return true; }
        private static bool Finite(Vector3 p) =>
            !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) &&
            !float.IsNaN(p.z) && !float.IsInfinity(p.z);
    }
}


