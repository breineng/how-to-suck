using System;
using System.Collections.Generic;
namespace HowToSuck.Networking
{
    // Lifecycle notification bookkeeping only: no NGO/Steam/game-rule dependencies.
    public sealed class UnexpectedStopSignal
    {
        public bool IsNotifying { get; private set; }
        public bool WasNotified { get; private set; }
        public void Reset()
        { if(IsNotifying)throw new InvalidOperationException("Cannot start a new lifecycle while notifying failure.");WasNotified=false; }
        public bool Notify(IEnumerable<Action> listeners,Action<Exception> report,Action shutdown)
        {
            if(WasNotified||IsNotifying)return false;
            if(shutdown==null)throw new ArgumentNullException(nameof(shutdown));
            WasNotified=true;IsNotifying=true;
            try
            {
                if(listeners!=null)foreach(var listener in listeners)
                {
                    try{listener?.Invoke();}
                    catch(Exception error){try{report?.Invoke(error);}catch{ /* Never skip remaining cleanup owners. */ }}
                }
            }
            finally{IsNotifying=false;shutdown();}
            return true;
        }
    }
}
