using System.Collections;

namespace HowToSuck
{
    public interface ISessionDriver
    {
        double Now { get; }
        IEnumerator Load(string sceneName);
        void Stop();
    }
}
