using System.Collections.Generic;
using System.Diagnostics;

namespace MCDSaveEdit.Services
{
    // Deliberately local and content-free. Callers may pass paths or save data;
    // never forward those values to a remote service or persist them implicitly.
    public static class EventLogger
    {
        public static void init() { }
        public static void dispose() { }
        public static void logEvent(string eventId, IDictionary<string, object>? fields = null) { }
        public static void logEvent(string eventId, double value) { }
        public static void logError(string message) => Debug.WriteLine("MCDSaveEdit operation failed; see the error dialog.");
    }
}
