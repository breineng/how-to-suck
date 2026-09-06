namespace HowToSuck
{
    // Implemented only by the composition owner; core UI does not depend on the networking assembly.
    public interface ISessionMenuExit
    {
        bool CanExitSessionMenu(SessionRoot session);
        bool ExitSessionMenu(SessionRoot session);
    }
}
