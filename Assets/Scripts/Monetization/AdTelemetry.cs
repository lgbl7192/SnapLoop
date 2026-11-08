using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Where ad decisions and lifecycle events go. Only a debug-log implementation exists; an analytics
    /// SDK can implement this later without touching the ad code.
    /// Events: session_start, session_background, consent_*, config_rejected, ad_gate_blocked,
    /// ad_decision, rewarded_offer, rewarded_start, rewarded_earned, rewarded_abandoned, ad_request,
    /// ad_loaded, ad_load_failed, ad_load_timeout, ad_displayed, ad_show_failed, ad_closed, ad_watchdog,
    /// banner_shown, banner_hidden.
    /// </summary>
    public interface IAdTelemetry
    {
        void Log(string eventName, string format = null, string placement = null, string detail = null);
    }

    public sealed class DebugLogAdTelemetry : IAdTelemetry
    {
        public void Log(string eventName, string format = null, string placement = null, string detail = null)
        {
            if (!Debug.isDebugBuild) return;
            var line = "[AdTelemetry] " + eventName;
            if (!string.IsNullOrEmpty(format)) line += " format=" + format;
            if (!string.IsNullOrEmpty(placement)) line += " placement=" + placement;
            if (!string.IsNullOrEmpty(detail)) line += " " + detail;
            Debug.Log(line);
        }
    }
}
