using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace HowToSuck
{
    // A UI color fade only; the right-hand background is rendered by the actual scene camera.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ConcreteMenuFade:MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;var left=(Color32)color;var right=left;right.a=0;
            vh.AddVert(new Vector3(r.xMin,r.yMin),left,Vector2.zero);vh.AddVert(new Vector3(r.xMin,r.yMax),left,Vector2.up);
            vh.AddVert(new Vector3(r.xMax,r.yMax),right,Vector2.one);vh.AddVert(new Vector3(r.xMax,r.yMin),right,Vector2.right);
            vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);
        }
    }
}
