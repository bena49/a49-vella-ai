using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace A49AIRevitAssistant
{
    public class GA_Tracking
    {
        private readonly string measurementId = "G-RMMYJTLEM3"; // Your GA Measurement ID
        private readonly string apiSecret = "osvo7Ys5SO6Ma7rgo2Zyww"; // Your GA API Secret

        // Method to trigger GA event
        public async Task SendAnalyticsEventWithLocationAsync(string eventName, string eventLabel)
        {
            // Get computer and user details
            string computerName = Environment.MachineName;
            string userName = Environment.UserName;

            // Get location data dynamically based on the user's public IP
            LocationData location = await GetLocationDataAsync();

            var eventData = new
            {
                client_id = Guid.NewGuid().ToString(), // Persistent client ID if required
                events = new[]
                {
                    new {
                        name = eventName,
                        @params = new
                        {
                            event_category = "AI_revit",
                            event_label = eventLabel,   // Use dynamic eventLabel passed from method call
                            value = 1,
                            computer_name = computerName,  // Send computer name
                            user_name = userName,          // Send user name
                            city = location.city,          // Send current city name dynamically
                            region = location.region,      // Send current region/state name dynamically
                            country = location.country,    // Send current country name dynamically
                            loc = location.loc,            // Latitude and longitude
                            postal = location.postal,      // Postal code
                            timezone = location.timezone,  // Timezone
                            ip_address = location.ip,      // Send public IP address dynamically
                            debug_mode = true              // Enable debug mode for DebugView
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(eventData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var client = new HttpClient())
            {
                var response = await client.PostAsync(
                    $"https://www.google-analytics.com/mp/collect?measurement_id={measurementId}&api_secret={apiSecret}",
                    content);

                if (!response.IsSuccessStatusCode)
                {
                    // Handle error
                    throw new Exception("Failed to send analytics event.");
                }
            }
        }

        // Utility method to fetch location data based on the user's public IP
        public async Task<LocationData> GetLocationDataAsync()
        {
            string ipinfoApiToken = "a9f6b651e9a0cc";  // Replace with your ipinfo.io API token

            using (HttpClient client = new HttpClient())
            {
                // Automatically fetch the location data based on the user's public IP
                var response = await client.GetStringAsync($"https://ipinfo.io/json?token={ipinfoApiToken}");
                return JsonConvert.DeserializeObject<LocationData>(response);
            }
        }

        // Class to store location data
        public class LocationData
        {
            public string ip { get; set; }       // Public IP address
            public string city { get; set; }     // City
            public string region { get; set; }   // State/Region
            public string country { get; set; }  // Country
            public string loc { get; set; }      // Latitude and longitude
            public string postal { get; set; }   // Postal code
            public string timezone { get; set; } // Timezone
        }
    }
}
