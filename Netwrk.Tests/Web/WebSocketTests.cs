using Microsoft.VisualStudio.TestTools.UnitTesting;
using Netwrk.Web;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Netwrk.Tests.Web
{
    [TestClass]
    public class WebSocketTests
    {
        private bool TryOpenListener(out NetwrkWebListener webListener)
        {
            webListener = new NetwrkWebListener(0);

            Assert.AreEqual(webListener.LocalAddress, IPAddress.Any, "LocalAddress used in the constructor should be set in the LocalAddress property");
            Assert.IsNull(webListener.Certificate, null, "Certificate should be null after the instanciation");
            Assert.IsFalse(webListener.Listening, "Listening should be false after instanciation");

            bool started = webListener.Start();

            Assert.AreNotEqual(webListener.Port, 0, "Port should not be 0");

            Assert.IsTrue(webListener.Listening, "Listening should be false after the call to Start");

            return started;
        }

        private bool TryConnectWebSocket(string uri, out NetwrkWebSocket webSocket)
        {
            webSocket = new NetwrkWebSocket();

            Assert.IsFalse(webSocket.Connected, "WebSocket.Connected should not be connected after instanciation");

            webSocket.ConnectAsync(new Uri(uri)).Wait();

            return webSocket.Connected;
        }
        
        [TestMethod]
        public void Connect()
        {
            Assert.IsTrue(TryOpenListener(out var webListener), "Failed to open WebSocket server");
            Assert.IsTrue(TryConnectWebSocket($"ws://localhost:{webListener.Port}", out var webSocket), "WebSocket client connection failed");
        }

        [TestMethod]
        public void ConnectDisconnect()
        {
            Assert.IsTrue(TryOpenListener(out var webListener), "Failed to open WebSocket server");

            ManualResetEvent serverDisconnection = new ManualResetEvent(false);

            webListener.OnWebSocketConnection += (l, w) => w.OnClose += (ww) => serverDisconnection.Set();

            Assert.IsTrue(TryConnectWebSocket($"ws://localhost:{webListener.Port}", out var webSocket), "WebSocket client connection");

            webSocket.Stop();

            Assert.IsFalse(webSocket.Connected, "WebSocket client should have Disconnected to false");
            Assert.IsTrue(serverDisconnection.WaitOne(2000), "Server side disconnection was not fired");
        }

        [TestMethod]
        public void ClientSendBinary()
        {
            Assert.IsTrue(TryOpenListener(out var webListener), "Failed to open WebSocket server");

            TaskCompletionSource<byte[]> dataReceived = new TaskCompletionSource<byte[]>();

            webListener.OnWebSocketConnection += (l, w) => w.OnBinaryMessage += (ww, data) => dataReceived.SetResult(data);

            byte[] randomData = new byte[128];
            Random random = new Random();

            random.NextBytes(randomData);

            Assert.IsTrue(TryConnectWebSocket($"ws://localhost:{webListener.Port}", out var webSocket), "WebSocket client connection failed");

            webSocket.Send(randomData);

            CollectionAssert.AreEqual(dataReceived.Task.Result, randomData, "Data received in the server was not the same as the data sent by the client");
        }

        [TestMethod]
        public void ServerSendBinary()
        {
            Assert.IsTrue(TryOpenListener(out var webListener), "Failed to open WebSocket server");

            TaskCompletionSource<byte[]> dataReceived = new TaskCompletionSource<byte[]>();
            ManualResetEvent sendEvent = new ManualResetEvent(false);

            byte[] randomData = new byte[128];
            Random random = new Random();

            webListener.OnWebSocketConnection += (l, w) =>
            {
                sendEvent.WaitOne();
                w.Send(randomData);
            };

            random.NextBytes(randomData);

            Assert.IsTrue(TryConnectWebSocket($"ws://localhost:{webListener.Port}", out var webSocket), "WebSocket client connection failed");

            webSocket.OnBinaryMessage += (ww, data) => dataReceived.SetResult(data);

            sendEvent.Set();

            CollectionAssert.AreEqual(dataReceived.Task.Result, randomData, "Data received in the server was not the same as the data sent by the client");
        }
    }
}
