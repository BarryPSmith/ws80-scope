using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace Ultrasonics
{
    internal class WebSource
    {
        string _uri;
        CancellationTokenSource _cts = new CancellationTokenSource();
        int _id;

        public WebSource(string uri = "wss://sierragliding.us/",
            int id = 72)
        {
            _id = id;
            _uri = uri;
            Task.Factory.StartNew(ReceiveLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        async Task ReceiveLoop()
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(_uri), _cts.Token);

            byte[] buffer = new byte[0x10000];
            try
            {
                while (true)
                {
                    MemoryStream ms = new MemoryStream();

                    WebSocketReceiveResult wsResult;
                    do
                    {
                        wsResult = await ws.ReceiveAsync(buffer, _cts.Token);
                        ms.Write(buffer, 0, wsResult.Count);
                    } while (!wsResult.EndOfMessage);
                    if (wsResult.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Seek(0, SeekOrigin.Begin);
                    ProcessMessage(ms);
                }
            }
            catch (TaskCanceledException) { }
        }

        private void ProcessMessage(MemoryStream ms)
        {
            StreamReader reader = new StreamReader(ms);

            var packet = JsonConvert.DeserializeObject<SierraGlidingPacket>(reader.ReadToEnd());

            if (packet == null)
                return;

            if (packet.id != _id || packet.op != SGOps.Add)
                return;

            PacketReceived?.Invoke(this, packet);
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public event EventHandler<SierraGlidingPacket>? PacketReceived;

        static HttpClient _client = new HttpClient();

        public static async Task<List<SierraGlidingPacket>> GetRangeAsync(DateTimeOffset start, DateTimeOffset end,
            double? statLen = null, 
            string url = "https://sierragliding.us/api/station/72/data",
            int sample=1)
        {
            var completeUrl = url + $"?start={start.ToUnixTimeSeconds()}&end={end.ToUnixTimeSeconds()}&sample={sample}";
            if (statLen.HasValue)
                completeUrl += $"&stat_len={statLen}";
            var resp = await _client.GetAsync(completeUrl);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Unable to get data ({resp.StatusCode})");
            var respStr = await resp.Content.ReadAsStringAsync();
            var respObj = JsonConvert.DeserializeObject<List<SierraGlidingPacket>>(respStr);
            if (respObj == null)
                return new List<SierraGlidingPacket>();
            return respObj;
        }
    }

    enum SGOps { Add, Remove }

    class SierraGlidingPacket
    {
        public SGOps op { get; set; }
        public int id { get; set; }
        public uint timestamp { get; set; }
        public double wind_direction { get; set; }
        public double wind_direction_avg { get; set; }
        public double windspeed { get; set; }
        public double windspeed_avg { get; set; }
        public double windspeed_min { get; set; }
        public double windspeed_max { get; set; }
    }
}
