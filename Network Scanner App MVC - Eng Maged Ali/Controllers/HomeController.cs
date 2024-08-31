using Microsoft.AspNetCore.Mvc;
using Network_Scanner_App_MVC___Eng_Maged_Ali.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web;
using YourNamespace; // Replace with your actual namespace

namespace YourNamespace.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> ScanNetwork()
        {
            string routerIP = GetDefaultGateway();
            string localIP = GetLocalIPAddress();

            if (routerIP == null || localIP == null)
            {
                ViewBag.Error = "Unable to get IP addresses";
                return View("Index");
            }

            string subnet = GetSubnet(routerIP);
            if (subnet == null)
            {
                ViewBag.Error = "Unable to get subnet";
                return View("Index");
            }

            List<Device> devices = await ScanNetwork(subnet, routerIP, localIP);

            // Pass the devices to the view
            ViewBag.Devices = devices;

            return View("Index");
        }

        private string GetDefaultGateway()
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                    {
                        if (gateway.Address.AddressFamily == AddressFamily.InterNetwork) // Only IPv4
                        {
                            return gateway.Address.ToString();
                        }
                    }
                }
            }
            return null;
        }

        private string GetLocalIPAddress()
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation unicastAddress in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return unicastAddress.Address.ToString();
                        }
                    }
                }
            }
            return null;
        }

        private string GetSubnet(string routerIP)
        {
            string[] ipParts = routerIP.Split('.');
            if (ipParts.Length == 4)
            {
                return $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}.";
            }
            return null;
        }

        private async Task<List<Device>> ScanNetwork(string subnet, string routerIP, string localIP)
        {
            List<Device> devices = new List<Device>();
            List<Task> tasks = new List<Task>();

            // Add Router first
            devices.Add(new Device
            {
                IPAddress = routerIP,
                HostName = "Router",
                Latency = await MeasureLatency(routerIP),
                MacAddress = GetMacAddress(routerIP)
            });

            // Add Local Device second
            devices.Add(new Device
            {
                IPAddress = localIP,
                HostName = "This Device",
                Latency = await MeasureLatency(localIP),
                MacAddress = GetMacAddress(localIP)
            });

            // Scan remaining devices
            for (int i = 1; i < 255; i++)
            {
                string ip = $"{subnet}{i}";
                if (ip == routerIP || ip == localIP)
                    continue;

                tasks.Add(Task.Run(async () =>
                {
                    if (await IsDeviceOnline(ip))
                    {
                        var device = new Device
                        {
                            IPAddress = ip,
                            HostName = GetHostName(ip),
                            Latency = await MeasureLatency(ip),
                            MacAddress = GetMacAddress(ip)
                        };

                        lock (devices)
                        {
                            devices.Add(device);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);
            return devices;
        }

        private async Task<bool> IsDeviceOnline(string ip)
        {
            try
            {
                using (var ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(ip, 1000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> MeasureLatency(string ip)
        {
            const int measurements = 5;
            List<long> latencies = new List<long>();

            try
            {
                using (var ping = new Ping())
                {
                    for (int i = 0; i < measurements; i++)
                    {
                        PingReply reply = await ping.SendPingAsync(ip, 1000);
                        if (reply.Status == IPStatus.Success)
                        {
                            latencies.Add(reply.RoundtripTime);
                        }
                        await Task.Delay(100);
                    }
                }

                if (latencies.Count > 0)
                {
                    long averageLatency = (long)latencies.Average();
                    return $"{averageLatency} ms";
                }
                else
                {
                    return "Request timed out";
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private string GetHostName(string ip)
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(ip);
                return hostEntry.HostName;
            }
            catch (SocketException)
            {
                return "Unknown";
            }
        }

        private string GetMacAddress(string ipAddress)
        {
            if (ipAddress == GetLocalIPAddress())
            {
                return GetLocalMacAddress();
            }
            else
            {
                try
                {
                    var arpProcess = new Process();
                    arpProcess.StartInfo.FileName = "arp";
                    arpProcess.StartInfo.Arguments = "-a";
                    arpProcess.StartInfo.RedirectStandardOutput = true;
                    arpProcess.StartInfo.UseShellExecute = false;
                    arpProcess.StartInfo.CreateNoWindow = true;
                    arpProcess.Start();

                    string output = arpProcess.StandardOutput.ReadToEnd();
                    arpProcess.WaitForExit();

                    string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[0] == ipAddress)
                        {
                            return parts[1];
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle exceptions if needed
                    Console.WriteLine(ex.Message);
                }
            }

            return "Unknown";
        }

        private string GetLocalMacAddress()
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    return nic.GetPhysicalAddress().ToString();
                }
            }
            return "Unknown";
        }
    }
}
