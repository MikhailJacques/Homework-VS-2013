using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace Homework
{
    public class MacsGroupedBySensorID
    {
        private Dictionary<int, List<string>> unique_wifi_devices = new Dictionary<int, List<string>>();
        
        public void Add(int key, string value)
        {
            if (this.unique_wifi_devices.ContainsKey(key))
            {
                List<string> list = this.unique_wifi_devices[key];
                if (list.Contains(value) == false)
                {
                    list.Add(value);
                }
            }
            else
            {
                List<string> list = new List<string>();
                list.Add(value);
                this.unique_wifi_devices.Add(key, list);
            }
        }

        public List<string> Retrieve(int key)
        {
            if (this.unique_wifi_devices.ContainsKey(key))
            {
                return this.unique_wifi_devices[key];
            }
            else
            {
                return null;
            }
        }


        public Queue<string> GetQueue()
        {
            Queue<string> unique_wifi_devices_queue = new Queue<string>();

            foreach (KeyValuePair<int, List<string>> pair in unique_wifi_devices)
            {
                unique_wifi_devices_queue.Enqueue(pair.Key.ToString());

                foreach (string mac in pair.Value)
                    unique_wifi_devices_queue.Enqueue(mac);
            }

            return unique_wifi_devices_queue;
        }


        public void Print(string sw_URI)
        {
            int unique_wifi_devices_count = 0;
            StreamWriter sw = new StreamWriter(sw_URI, false);

            sw.WriteLine(DateTime.Now);

            foreach (KeyValuePair<int, List<string>> pair in unique_wifi_devices)
            {
                sw.WriteLine("sensor_id: {0}", pair.Key);

                unique_wifi_devices_count += pair.Value.Count;

                foreach (string mac in pair.Value)
                    sw.WriteLine("mac address: {0}", mac);
            }

            sw.WriteLine(" ------- ");
            sw.WriteLine("\nTotal number of unique sensors {0}", unique_wifi_devices.Count);
            sw.WriteLine("Total number of unique WiFi devices {0}", unique_wifi_devices_count);

            sw.Flush();
        }
    }
}