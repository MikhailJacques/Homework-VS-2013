using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Security.AccessControl;

namespace Homework
{
    class FinalOutput
    {
        private StreamWriter output_file;
        private Object thisLock = new Object();
        private Dictionary<string, Device> unique_wifi_devices;

        public FinalOutput(StreamWriter output_file, Dictionary<string, Device> unique_wifi_devices)
        {
            this.output_file = output_file;
            this.unique_wifi_devices = unique_wifi_devices;
        }

        static private Dictionary<String, Object> LockObjects = new Dictionary<string, object>();

        static public void Write(string message, string outputFile)
        {
            if (!LockObjects.ContainsKey(outputFile))
            {
                LockObjects.Add(outputFile, new Object());
            }

            lock (LockObjects[outputFile])
            {
                using (FileStream fs = new FileStream(outputFile, FileMode.Append, 
                    FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(message);
                    }
                }
            }
        }


        public void PrintUniquePairs()
        {
            lock (thisLock)
            {
                Console.WriteLine("{0} has entered the output object", Thread.CurrentThread.ManagedThreadId);

                foreach (KeyValuePair<string, Device> pair in unique_wifi_devices)
                    output_file.WriteLine("sensor_id: {0} mac address: {1}", pair.Value.sensor_id, pair.Value.mac);

                output_file.WriteLine("\nTotal number of unique WiFi devices {0}", unique_wifi_devices.Count);

                output_file.Flush();
            }
        }

        public void Print()
        {
            for (int i = 0; i < 100; i++)
            {
                // Ky
            }
        }
    }
}
