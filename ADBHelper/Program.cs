using AdvancedSharpAdbClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace ADBHelper
{
    internal class Program
    {
        // Note : ADBHelper.exe should be placed at the same folder with adb.exe as
        // wellas scrcpy.exe
        const string port = "5555";
        const string adbFileName = @".\adb.exe";
        const string scrcpyPath = @" /k .\scrcpy.exe";

        public IEnumerable<DeviceData> devices = new AdbClient().GetDevices();

        static string GetDeviceIpAddress(DeviceData device)
        {
            using (Process process = new Process())
            {
                string arg = "-s " + device.Serial + " " + "shell ip route";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = adbFileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = arg
                };

                process.StartInfo = startInfo;
                process.Start();

                // Read the output and error streams
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                // Handle the output and error as needed
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"Error: {error}");
                    return null;
                }

                // Parse the output to extract the IP address
                string[] lines = output.Split('\n');
                foreach (string line in lines)
                {
                    if (line.Contains("src"))
                    {
                        string[] tokens = line.Split();
                        for (int i = 0; i < tokens.Length; i++)
                        {
                            if (tokens[i] == "src")
                            {
                                return tokens[i + 1];
                            }
                        }
                    }
                }
                Console.WriteLine("Failed to retrieve IP address.");
                return null;
            }
        }

        bool IsDeviceOnlineInTCPIPMode(DeviceData device)
        {
            // check if the device is already connected via TCPIP
            var ipAddress = GetDeviceIpAddress(device);

            if (ipAddress != null)
            {
                string ipAsSerial = ipAddress + ":" + port;

                foreach (var d in devices)
                {
                    if (d.Serial == ipAsSerial
                        && d.State == AdvancedSharpAdbClient.DeviceState.Online)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        void SwitchToWireless()
        {
            Regex rx = new Regex(@"^(?<host>.+):(?<port>\d+)$");

            foreach (var device in devices)
            {
                Match m = rx.Match(device.Serial);
                if (m.Success)
                {
                    // ADB is already in TCPIP connection mode
                    Console.WriteLine("device "
                          + device.Serial + " is now connected in wireless mode.");
                    continue;
                }
                else
                {
                    string ip = GetDeviceIpAddress(device);

                    if (IsDeviceOnlineInTCPIPMode(device))
                    {
                        Console.WriteLine("You may now disconnect the USB cable for device "
                            + device.Serial + ", it is now connected in TCPIP mode at " + ip);
                        continue;
                    }

                    Console.WriteLine("device "
                            + device.Serial +
                            " is now connected in USB mode, swithcing to TCPIP mode ...");

                    // switch to TCPIP mode
                    var psi = new ProcessStartInfo();
                    psi.FileName = adbFileName;
                    psi.Arguments = @"-s " + device.Serial + " tcpip " + port;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true; ;
                    Process.Start(psi);

                    // take it online in TCPIP mode
                    psi.Arguments = @"-s " + device.Serial +
                        " connect " + ip + ":" + port;

                    Process.Start(psi);
                    System.Threading.Thread.Sleep(7000);

                    Console.WriteLine(IsDeviceOnlineInTCPIPMode(device) ? "done." : "failed.");
                }
            }
        }

        void ConnectDevices()
        {
            // command
            //"cmd /k E:\Software\scrcpy-win64-v2.3.1\scrcpy.exe --no-audio
            //          --serial 192.168.1.7 --window-x 200 --window-y 100 --window-width 600 --window-height 1200"

            // connection parameters of ScrCpy.exe
            var noaudio = " --no-audio";
            var sn = " --serial";
            var w_x = " --window-x";
            var w_y = " --window-y";
            var w_w = " --window-width";
            var w_h = " --window-height";

            // the following window sizes and coordinates are arbitrary on a 4K display
            // ajust it to accordingly to your own relsolution.
            int x = 200;
            int y = 100;
            int width = 600;
            int height = 1200;

            Regex rx = new Regex(@"^(?<host>.+):(?<port>\d+)$");

            foreach (var device in devices)
            {
                Match m = rx.Match(device.Serial);
                if (!m.Success)
                {
                    // skip these USB connected devices.
                    continue;
                }

                string host = m.Groups["host"].Value;
                int port = int.Parse(m.Groups["port"].Value);

                string arg = scrcpyPath + noaudio + sn + " "
                    + host
                    + w_x + " " + x.ToString()
                    + w_y + " " + y.ToString()
                    + w_w + " " + width.ToString()
                    + w_h + " " + height.ToString();

                // connect
                Console.WriteLine("Mirroring " + device.Serial + "...");
                var psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = arg;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true; ;
                Process.Start(psi);

                System.Threading.Thread.Sleep(3000);
                Console.WriteLine("done.");
                x += 600;
            }
        }

        static void Main(string[] args)
        {
            Program p = new Program();

            if (p.devices.Count<DeviceData>() <= 0)
            {
                Console.WriteLine("No device is attached.");
            }
            else
            {
                Console.WriteLine("Attached device(s)  \n" +
                        "---------------------");

                foreach (var device in p.devices)
                {
                    Console.WriteLine(device.Serial + " - " + device.Model);
                }

                Console.WriteLine("---------------------");
            }

            if (args.Length == 0)
            {
                Console.WriteLine("\n" +
                    "Usage : \n" +
                      "   ADBHelper wireless  - switch USB connected ADB devices to TCPIP (wireless) mode \n" +
                      "   ADBHelper connect   - connect all devices for screen mirroring");
                return;
            }

            // start ADB server.
            if (!AdbServer.Instance.GetStatus().IsRunning)
            {
                Console.WriteLine("Starting adb server...");
                AdbServer server = new AdbServer();
                StartServerResult result = server.StartServer(@".\adb.exe", false);
                if (result != StartServerResult.Started)
                {
                    Console.WriteLine("Can't start adb server");
                }
                else
                {
                    Console.WriteLine("done.");
                }
            }

            switch (args[0])
            {
                case "wireless":
                    p.SwitchToWireless();
                    break;

                case "connect":
                    p.ConnectDevices();
                    break;

                default:
                    break;
            }
        }
    }
}
