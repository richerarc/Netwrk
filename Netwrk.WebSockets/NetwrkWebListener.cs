using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Netwrk.WebSockets
{
	public class NetwrkWebListener
	{
		public delegate NetwrkWebResponse RequestEventHandler(NetwrkWebListener listener, NetwrkWebRequest request);
		public delegate bool WebSocketRequestEventHandler(NetwrkWebListener listener, NetwrkWebRequest request);
		public delegate void WebSocketConnectionEventHandler(NetwrkWebListener listener, NetwrkWebSocket webSocket);

		private TcpListener listener;

		public IPAddress LocalAddress { get; }
		public int Port { get; }
		public X509Certificate2 Certificate { get; }

		public event RequestEventHandler OnRequest;
		public event WebSocketRequestEventHandler OnWebsocketRequest;
		public event WebSocketConnectionEventHandler OnWebSocketConnection;

		public NetwrkWebListener(int port, IPAddress localAddress = null, X509Certificate2 certificate = null)
		{
			Port = port;
			LocalAddress = localAddress ?? IPAddress.Any;
			Certificate = certificate;
		}

		public void Start()
		{
			if (listener != null)
			{
				return;
			}

			listener = new TcpListener(LocalAddress, Port);
			listener.Start();

			AcceptClients();
		}

		public void Stop()
		{
			if (listener == null)
			{
				return;
			}

			listener.Stop();
			listener = null;
		}

		private async void AcceptClients()
		{
			while (true)
			{
				TcpClient client = await listener.AcceptTcpClientAsync();

				client.NoDelay = true;

				var t = Task.Run(async delegate
				{
					if (!await HandleClient(client))
					{
						client.Close();
					}
				});
			}
		}

		private async Task<bool> HandleClient(TcpClient client)
		{
            NetwrkWebClient webClient = new NetwrkWebClient(client);

			if (Certificate != null)
			{
				await webClient.SslAuthenticateAsServerAsync(Certificate);
			}

			NetwrkWebRequest request = await webClient.ReceiveAsync<NetwrkWebRequest>();

			if (request == null)
			{
				return false;
			}

			if (request.IsWebSocketRequest)
			{
				if (OnWebsocketRequest?.Invoke(this, request) ?? true)
				{
					NetwrkWebResponse response = new NetwrkWebResponse
					{
						Version = request.Version,
						StatusCode = NetwrkHttpStatusCode.SwitchingProtocols
					};

					response.Headers[NetwrkKnownHttpHeaders.Upgrade] = "websocket";
					response.Headers[NetwrkKnownHttpHeaders.Connection] = "Upgrade";
					response.Headers[NetwrkKnownHttpHeaders.Server] = "Netwrk/1.0";

					using (var sha1 = SHA1.Create())
					{
						string accept = request.Headers[NetwrkKnownHttpHeaders.SecWebSocketKey] + NetwrkWebSocket.ConstantKey;
						byte[] acceptBytes = Encoding.ASCII.GetBytes(accept);
						byte[] acceptSha1 = sha1.ComputeHash(acceptBytes);
                        
						response.Headers[NetwrkKnownHttpHeaders.SecWebSocketAccept] = Convert.ToBase64String(acceptSha1);
					}

					await webClient.SendAsync(response);

					NetwrkWebSocket webSocket = new NetwrkWebSocket(client, webClient.Stream);

					OnWebSocketConnection?.Invoke(this, webSocket);

					webSocket.Start();
				}
			}
			else
			{
				NetwrkWebResponse response = OnRequest?.Invoke(this, request) ?? new NetwrkWebResponse
				{
					Version = request.Version,
					StatusCode = NetwrkHttpStatusCode.InternalServerError
				};

				await webClient.SendAsync(response);

				client.Close();
			}

			return true;
		}
	}
}
