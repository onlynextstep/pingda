namespace PingXu.Core;
public static class RenderFailure
{
    public static bool IsFatal(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current.HResult == unchecked((int)0x88980406)) return true;
        return false;
    }
}
