using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using System.IO;
using System.Xml;

namespace nanoFrameworkFlasher
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("== FOUND THESE PORTS == ");

            List<byte[]> assemblies = new List<byte[]>();
            int retryCount = 0;
            int _numberOfRetries = 100;

            var serialDebugClient = PortBase.CreateInstanceForSerial(true, null);

        retryConnection:
            while (!serialDebugClient.IsDevicesEnumerationComplete)
            {
                Thread.Sleep(1);
            }

            Console.WriteLine($"Found: {serialDebugClient.NanoFrameworkDevices.Count} devices");

            if (serialDebugClient.NanoFrameworkDevices.Count == 0)
            {
                if (retryCount > _numberOfRetries)
                {
                    Console.WriteLine("ERRROR TOO MANY RETRIES");
                }
                else
                {
                    retryCount++;
                    serialDebugClient.ReScanDevices();
                    goto retryConnection;
                }
            }

            retryCount = 0;
            NanoDeviceBase device = serialDebugClient.NanoFrameworkDevices[0];

            //BartJ TODO account for multiple connected devices: 
            // if (serialDebugClient.NanoFrameworkDevices.Count > 1)
            // {
            //     device = serialDebugClient.NanoFrameworkDevices.Where(m => m.SerialNumber == port).First();
            // }
            // else
            // {
            //     device = serialDebugClient.NanoFrameworkDevices[0];
            // }

            Console.WriteLine($"Getting things with {device.Description}");

            // check if debugger engine exists
            if (device.DebugEngine == null)
            {
                device.CreateDebugEngine();
                Console.WriteLine($"Debug engine created.");
            }

        retryDebug:
            bool connectResult = device.DebugEngine.Connect(5000, true, true);
            Console.WriteLine($"Device connect result is {connectResult}. Attempt {retryCount}/{_numberOfRetries}");

            if (!connectResult)
            {
                if (retryCount < _numberOfRetries)
                {
                    // Give it a bit of time
                    await Task.Delay(100);
                    retryCount++;
                    goto retryDebug;
                }
                else
                {
                    Console.WriteLine("ERRROR TOO MANY RETRIES");
                }
            }

            retryCount = 0;

        retryErase:
            // erase the device
            Console.WriteLine(($"Erase deployment block storage. Attempt {retryCount}/{_numberOfRetries}."));

            var eraseResult = device.Erase(
                    EraseOptions.Deployment,
                    null,
                    null);

            Console.WriteLine(($"Erase result is {eraseResult}."));
            if (!eraseResult)
            {
                if (retryCount < _numberOfRetries)
                {
                    // Give it a bit of time
                    await Task.Delay(400);
                    retryCount++;
                    goto retryErase;
                }
                else
                {
                    Console.WriteLine("COULDNT ERASE. TOO MANY RETRIES");
                }
            }

            // build a list with the full path for each DLL, referenced DLL and EXE
            List<String> assemblyList = new List<String>();

            var workingDirectory = Path.GetDirectoryName(@"/Users/bartjansen/htdocs/nanoFrameworkFlasher/bin/Debug/testPEs/");
            var peFiles = Directory.GetFiles(workingDirectory, "*.pe");

            Console.WriteLine($"Added {peFiles.Length} assemblies to deploy.");

            // Keep track of total assembly size
            long totalSizeOfAssemblies = 0;

            // now we will deploy all system assemblies
            foreach (String peItem in peFiles)
            {
                // append to the deploy blob the assembly
                using (FileStream fs = File.Open(Path.Combine(workingDirectory, peItem), FileMode.Open, FileAccess.Read))
                {
                    long length = (fs.Length + 3) / 4 * 4;
                    Console.WriteLine($"Adding {peItem} v0 ({length} bytes) to deployment bundle");
                    byte[] buffer = new byte[length];

                    await fs.ReadAsync(buffer, 0, (int)fs.Length);
                    assemblies.Add(buffer);

                    // Increment totalizer
                    totalSizeOfAssemblies += length;
                }
            }

            Console.WriteLine($"Deploying {peFiles.Length:N0} assemblies to device... Total size in bytes is {totalSizeOfAssemblies}.");

            // need to keep a copy of the deployment blob for the second attempt (if needed)
            var assemblyCopy = new List<byte[]>(assemblies);

            var deploymentLogger = new Progress<string>((m) => Console.WriteLine(m));
        
            if (!device.DebugEngine.DeploymentExecute(
                assemblyCopy,
                false,
                false,
                null,
                deploymentLogger))
            {
                Console.WriteLine("WRITE FAILED :(");
            }
            else
            {
                Console.WriteLine("WRITE SUCCEEDED :)");
            }
        }
    }
}
