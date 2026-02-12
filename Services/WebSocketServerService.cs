using Fleck;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System;

namespace AoE4OverlayCS.Services
{
    public class WebSocketServerService
    {
        private WebSocketServer? _server;
        private readonly List<IWebSocketConnection> _sockets = new();
        private readonly List<string> _messageHistory = new();
        private int _port;

        public WebSocketServerService(int port)
        {
            _port = port;
        }

        public void Start()
        {
            try 
            {
                // Try to bind to port, if fails, increment (basic implementation)
                // Fleck logs to Console by default.
                _server = new WebSocketServer($"ws://0.0.0.0:{_port}");
                _server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        lock (_sockets) _sockets.Add(socket);
                        // Send history
                        lock (_messageHistory)
                        {
                            if (_messageHistory.Any())
                            {
                                socket.Send(_messageHistory.First());
                                if (_messageHistory.Count > 1)
                                    socket.Send(_messageHistory.Last());
                            }
                        }
                    };
                    socket.OnClose = () =>
                    {
                        lock (_sockets) _sockets.Remove(socket);
                    };
                });
            }
            catch (Exception)
            {
                // Simple retry logic could go here
            }
        }

        public void Stop()
        {
            _server?.Dispose();
        }

        public void Send(string type, object data)
        {
            var msg = new { type, data };
            var json = JsonConvert.SerializeObject(msg);
            
            lock (_messageHistory)
            {
                _messageHistory.Add(json);
                // Keep history small
                if (_messageHistory.Count > 50) _messageHistory.RemoveAt(0);
            }

            lock (_sockets)
            {
                foreach (var socket in _sockets.ToList())
                {
                    socket.Send(json);
                }
            }
        }
    }
}
