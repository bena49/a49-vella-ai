// ============================================================================
// DjangoBridge.cs (FIXED)
// 1. Uses JObject instead of dynamic to prevent RuntimeBinder exceptions.
// 2. Uses ConfigureAwait(false) to prevent Revit UI Thread Deadlocks.
// 3. Injects Azure SSO Bearer Token for secure Django communication.
// ============================================================================
using System.Net.Http;
using System.Net.Http.Headers; // 💥 NEW: Required for AuthenticationHeaderValue
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor
{
    public static class DjangoBridge
    {
        private static readonly HttpClient _client = new HttpClient();
        // Ensure this URL is reachable and correct
        private const string ApiUrl = "https://a49iris.com/irisai-api/api/ai/";

        // 💥 NEW: Store the active token here for the session
        public static string CurrentToken { get; set; }

        // Return Task<JObject> instead of dynamic for type safety
        public static async Task<JObject> SendAsync(object payload)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload);

                // 💥 NEW: Use HttpRequestMessage to safely attach the header per-request
                var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                // 💥 NEW: Attach the VIP Pass if we have it!
                if (!string.IsNullOrEmpty(CurrentToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken);
                }

                // ConfigureAwait(false) prevents deadlock on Revit UI Thread
                var response = await _client.SendAsync(request).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Deserialize to JObject safely
                return JObject.Parse(responseString);
            }
            catch (System.Exception ex)
            {
                A49Logger.Log($"❌ DjangoBridge Error: {ex.Message}");
                return null;
            }
        }
    }
}