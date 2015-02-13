// By Mikhail Jacques 
// Date: December 1, 2014
// https://github.com/MikhailJacques
// https://www.linkedin.com/in/mikhailjacques

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;
using System.Timers;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Security.Principal;

namespace Homework
{
    class Program
    {
        private const bool DEBUG = false;
        private const int THREAD_POOL_SIZE = 10;
        private const int RSSI_UPPER_LIMIT = -30;
        private const int RSSI_LOWER_LIMIT = -90;
        private const int TIMER_TIME_INTERVAL = 1000;       // Time interval at which timer fires (in milliseconds)
        private const int NUM_TIMER_EVENTS_TO_FIRE = 5;     // Number of timer events to be fired and executed
        private const int MAX_LATEST_WiFi_DEVICES = 1000;   // Maximum number of latest up-to-date detected WiFi devices
        private const string MAC_LIST_URI = "http://api.analoc.com/test/mac-list";
        private const string OUI_VENDOR_URI = "http://standards-oui.ieee.org/oui.txt";
        // private const string OUI_VENDOR_URI = "http://www.ieee.org/netstorage/standards/oui.txt"; // OLD URL

        private static string LOG_DIR = null;
        private static string LOG_FILE_URI_DEVICES_DISTINCT = null;
        private static string LOG_FILE_URI_AS_IS_SEQUENTIAL = null;
        private static string LOG_FILE_URI_GROUPED_SEQUENTIAL = null;
        private static string LOG_FILE_URI_GROUPED_MULTITHREADING = null;

        private static int threshold;                       // User-defined threshold value
        private static int num_events_fired;                // Keeps track of the number of timer events fired
        private static System.Timers.Timer timer;           // Self-explanatory
        private static Mutex mtx = new Mutex();             // Synchronization primitive
        private static Object locker = new Object();        // Synchronization primitive

        private static ManualResetEvent all_done = new ManualResetEvent(false);
        private static HashSet<Device> devices_distinct = new HashSet<Device>();
        private static MacsGroupedBySensorID macs_by_sensor_id = new MacsGroupedBySensorID();
        private static Dictionary<string, Object> lock_objects = new Dictionary<string, object>();
        private static Dictionary<string, string> oui_to_vendor = new Dictionary<string, string>();
        private static Dictionary<string, Device> unique_WiFi_devices = new Dictionary<string, Device>();

        static void Main(string[] args)
        {
            try
            {
                // Install-time program settings.
                // The CommonApplicationData directory serves as a common repository for application-specific data that 
                // is used by all users. Normally, the following commented out statement returns "C:\\ProgramData"
                // Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

                // Set path for directory where the log files will be stored
                LOG_DIR = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    + @"\Analoc\Homework\Logs";

                // When the following line of code execute it creates the directory only if it does not exist.
                // Currently only the Wix Installer creates this directory during the installation of the application
                // on the target computer and prior to executing it for the first time.
                DirectoryInfo dInfo = Directory.CreateDirectory(LOG_DIR);

                // Set paths for the log files
                LOG_FILE_URI_DEVICES_DISTINCT = LOG_DIR + "\\DistinctWiFiDevicesAsIs_REC.txt";
                LOG_FILE_URI_AS_IS_SEQUENTIAL = LOG_DIR + "\\DistinctWiFiDevicesAsIs_SEQ.txt";
                LOG_FILE_URI_GROUPED_SEQUENTIAL = LOG_DIR + @"\DistinctWiFiDevicesGroupedBySensorID_SEQ.txt";
                LOG_FILE_URI_GROUPED_MULTITHREADING = LOG_DIR + @"\DistinctWiFiDevicesGroupedBySensorID_MUL.txt";

                // Currently the following does not seem to be necessary
                //DirectorySecurity dSecurity = dInfo.GetAccessControl();
                //dSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                //    FileSystemRights.FullControl, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                //    PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                //dInfo.SetAccessControl(dSecurity);
            }

            catch (Exception e)
            {
                Console.WriteLine("{0} Exception caught.", e);
                Console.ReadKey();
            }

            // Due to the computationally intensive nature of the task of fetching, parsing
            // and storing mapped Vendor data elevate the current process priority to High.
            using (Process p = Process.GetCurrentProcess())
                p.PriorityClass = ProcessPriorityClass.High;

            // Retrieve from web a list of Vendor names along with their respective Organizationally Unique Identifiers 
            // (OUIs) and save them in a dictionary data structure using OUI as a key and Vendor name as a value.
            // Fork a separate thread to fetch the vendor data in the background, while obtaining user input.
            // Note that declaration of a ThreadStart delegate is not strictly necessary.
            ThreadStart vendorNamesRef = new ThreadStart(GetVendorNames);
            Thread vendorNames = new Thread(vendorNamesRef);
            vendorNames.Name = "Vendor Names";
            vendorNames.Priority = ThreadPriority.Highest;
            vendorNames.IsBackground = true;
            vendorNames.Start();

            // Obtain input parameters from the damn user.
            threshold = GetUserInput();

            // Block the main thread until vendorNames thread finishes execution.
            vendorNames.Join();

            Console.WriteLine("\nFinished collecting Vendor Organizationally Unique Identifiers Data.\n\n" +
                "Commencing sampling and processing WiFi devices in real time.");

            // Obtain first sample of the list of most recent detected WiFi device records.
            devices_distinct.UnionWith(GetLatestDetectedWiFiDevices());

            // Keep track of all the unique WiFi devices that have been detected throughout the execution of the program.
            foreach (var device in devices_distinct)
            {
                if (!unique_WiFi_devices.ContainsKey(device.mac))
                {
                    unique_WiFi_devices.Add(device.mac, device);
                    macs_by_sensor_id.Add(device.sensor_id, device.mac);
                }
                //else
                //{
                //    if (DEBUG)
                //        Console.WriteLine("Duplicate entry {0}", device.mac);
                //}
            }

            //if (DEBUG)
            //{
            //    Console.WriteLine("\nNumber of WiFi devices in devices_distinct {0}", devices_distinct.Count);
            //    Console.WriteLine("Number of WiFi devices in unique_WiFi_devices {0}\n", unique_WiFi_devices.Count);
            //}

            // Initialize timing object
            Timing timing = new Timing(TIMER_TIME_INTERVAL, NUM_TIMER_EVENTS_TO_FIRE, LOG_FILE_URI_DEVICES_DISTINCT);

            // Fork a new thread to fetch the WiFi devices data.
            Thread devicesWiFi = new Thread(StartDevicesRetrieval);
            devicesWiFi.Name = "WiFi Devices";
            devicesWiFi.Priority = ThreadPriority.Highest;
            devicesWiFi.Start(timing);

            // Stall the main thread until devicesWiFi thread has finished its execution.
            devicesWiFi.Join();

            using (Process p = Process.GetCurrentProcess())
                p.PriorityClass = ProcessPriorityClass.Normal;


            // ---------------------------------------------------------------------------------------------------------
            // Insert into a text file all the unique records (sensor ID, mac address) of WiFi devices 
            // that have been collected during the execution of the application in the order they were collected.

            // Open the stream writer.
            StreamWriter sw = new StreamWriter(LOG_FILE_URI_AS_IS_SEQUENTIAL, false, Encoding.UTF8, 1024);

            // Time stamp the output file.
            sw.WriteLine(DateTime.Now);

            foreach (KeyValuePair<string, Device> pair in unique_WiFi_devices)
                sw.WriteLine("sensor_id: {0} mac address: {1}", pair.Value.sensor_id, pair.Value.mac);

            sw.WriteLine(" ------- ");
            sw.WriteLine("Total number of unique WiFi devices {0}", unique_WiFi_devices.Count);

            sw.Flush();
            //
            // ---------------------------------------------------------------------------------------------------------



            // ---------------------------------------------------------------------------------------------------------
            // Insert into a text file all the unique records (sensor ID, mac address) of WiFi devices 
            // that have been collected during the execution of the application grouped by sensor ID.
            macs_by_sensor_id.Print(LOG_FILE_URI_GROUPED_SEQUENTIAL);
            //
            // ---------------------------------------------------------------------------------------------------------



            // ---------------------------------------------------------------------------------------------------------
            // Insert into a text file all the unique records (sensor ID, mac address) of WiFi devices 
            // that have been collected during the execution of the application grouped by sensor ID but
            // use multiple threads to accomplish this task as described below:

            // Open a thread pool of the specified number of threads and insert asynchronously into a text file 
            // all of the unique (mac addresses, sensor) pairs that have been collected grouped by sensor ID.
            // Each thread inserts a group of mac addresses sent by a single sensor ID until there are no more 
            // macs associated with this particular sensor ID at which point the thread terminates and passes the 
            // ball to the next thread which in turn performs the same operation on the group of macs associated
            // with the next sensor ID and so on and so forth until there are no more data left. 
            // The groups of mac addresses are separated by a line indicating the sensor ID number.
            Thread[] threads = new Thread[THREAD_POOL_SIZE];

            // Reset log files interlocking mechanism.
            lock_objects.Clear();

            // Get the list of groups of unique WiFi devices prepended with their sensor IDs in one single queue.
            Queue<string> unique_WiFi_devices_queue = macs_by_sensor_id.GetQueue();

            // Create specified number of threads
            for (int i = 0; i < THREAD_POOL_SIZE; i++)
                threads[i] = new Thread(() => PrintSummary(unique_WiFi_devices_queue, LOG_FILE_URI_GROUPED_MULTITHREADING));

            // Start the threads
            for (int i = 0; i < THREAD_POOL_SIZE; i++)
                threads[i].Start();

            // ---------------------------------------------------------------------------------------------------------

            // Have some rest
            Thread.Sleep(TIMER_TIME_INTERVAL);

            // Say bye
            Task.Factory.StartNew(End);

            // Wait for user to acknowledge the termination of the application
            Console.ReadKey();
        }


        // Purpose: To print to a text file all the unique records (sensor ID, mac address) of WiFi devices 
        //          that have been collected during the execution of the application grouped by sensor ID.
        //          This method is invoked recurrently by the THREAD_POOL_SIZE number of threads. 
        //          Therefore each element once printed out is removed from the Queue collection.
        private static void PrintSummary(Queue<string> devices, string log_file_uri)
        {
            if (!lock_objects.ContainsKey(log_file_uri))
                lock_objects.Add(log_file_uri, new Object());

            lock (lock_objects[log_file_uri])
            {
                using (FileStream fs = new FileStream(log_file_uri, FileMode.Append,
                    FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        while (devices != null && devices.Count > 0)
                        {
                            // Check to see whether it is a sensor ID or a mac address.
                            if (devices.Peek().Length < 17)
                            {
                                //if (DEBUG)
                                //    sw.WriteLine("Pool Thread ID: {0}", Thread.CurrentThread.ManagedThreadId);

                                sw.Write("sensor_id: ");
                                Thread.Yield();
                            }
                            else
                                sw.Write("mac address: ");

                            sw.WriteLine(devices.Dequeue() + "\n");
                        }
                    }
                }
            }
        }


        // Purpose: To retrieve a list of WiFi device Vendor/Manufacturer names along with their associated 
        //          24-bit Organizationally Unique Identifiers, parse them out and store the key-value pairs 
        //          in a dictionary data structure for later reference.
        //          It is a one-time computationally intensive operation that is performed only once at the 
        //          beginning of application execution.
        private static void GetVendorNames()
        {
            // The using statement automatically invokes the IDisposable.Dispose on the object.
            using (WebClient client = new WebClient())
            {
                string oui = null, vendor = null;
                // string line = null, oui = null, vendor = null;

                // Stream stream = client.OpenRead(OUI_VENDOR_URI);

                // Open the file to read from. 
                string[] all_lines = File.ReadAllLines(@"..\docs\oui.txt");
                // using (StreamReader reader = new StreamReader(@"..\..\docs\oui.txt"))
                //using (StreamReader reader = new StreamReader(stream, Encoding.Default))
                //{
                // Read the stream one line at a time
                //while ((line = reader.ReadLine()) != null)
                foreach (string line in all_lines)
                {
                    // Example of a valid line: 
                    // "  00-00-01   (hex)\t\tXEROX CORPORATION"
                    if (line.IndexOf("(hex)") != -1)
                    {
                        string[] parts = FormatRecord(line);

                        // Save the Organizationally Unique Identifier 
                        // (24-bit number that uniquely identifies a vendor/manufacturer).
                        oui = parts[0];

                        // Compose and save the vendor name.
                        for (int i = 1; i < parts.Length; i++)
                        {
                            vendor += parts[i];

                            if (i < parts.Length - 1)
                                vendor += " ";
                        }

                        // Map the 'OUI' to 'Vendor' in a key-value dictionary collection.
                        // Ignore duplicate keys by simply not entering them into the dictionary.
                        // Example of real duplicate keys:
                        // 08-00-30 NETWORK RESEARCH CORPORATION
                        // 08-00-30 CERN
                        // 08-00-30 ROYAL MELBOURNE INST OF TECH
                        if (!oui_to_vendor.ContainsKey(oui))
                            oui_to_vendor.Add(oui, vendor);

                        oui = null; vendor = null;
                    }
                }
                //}
            }
        }


        // Purpose: To retrieve a list of the specified number of latest up-to-date 
        //          detected WiFi devices and to store them in a HashMap collection.
        private static HashSet<Device> GetLatestDetectedWiFiDevices()
        {
            HashSet<Device> devices_sample = new HashSet<Device>();

            // Use using since WebClient is IDisposable
            using (WebClient client = new WebClient())
            {
                List<Device> devices = new List<Device>();

                string data = null, oui = null, vendor = null;

                // Load JSON data from the specified URL.
                data = client.DownloadString(MAC_LIST_URI);

                // De-serialize JSON data into a List of Device objects.
                devices = JsonConvert.DeserializeObject<List<Device>>(data);

                // Get only the specified number of the latest detected WiFi devices.
                foreach (var device in devices.Take(MAX_LATEST_WiFi_DEVICES))
                {
                    // Parse out the OUI
                    oui = device.mac.Substring(0, 8).Replace(":", string.Empty);

                    // Look up Vendor name in the dictionary.
                    if (oui_to_vendor.TryGetValue(oui, out vendor))
                        device.vendor = vendor;

                    data = null; oui = null; vendor = null;

                    devices_sample.Add(device);
                }
            }

            return devices_sample;
        }


        // Purpose: To process a batch of newly detected WiFi devices as per the application specifications.
        private static void ProcessDevicesBatch(Object source, ElapsedEventArgs e,
            int num_events_to_fire, string log_file_uri)
        {
            // Check to see if the timer has fired the specified number of times.
            if (++num_events_fired >= num_events_to_fire)
                timer.Stop();

            // Obtain a list of the specified number of latest up-to-date detected WiFi devices.
            // Note that this operation pre-fetches the list while potentially waiting for the mutex 
            // to be released thus speeding up the execution of the thread.
            HashSet<Device> devices_sample = new HashSet<Device>(GetLatestDetectedWiFiDevices());

            //if (DEBUG)
            //    Console.WriteLine("Number of WiFi devices in devices_sample {0}", devices_sample.Count);

            // Wait until it is safe to enter the critical section.
            if (mtx.WaitOne())
            {
                //if (DEBUG)
                //    Console.WriteLine("{0} has acquired the mutex", Thread.CurrentThread.ManagedThreadId);

                // High-level description of the algorithm:
                // - Maintain a list of sampled devices.
                // - At each new sample compare the new sample list with the current list.
                // - Add newly detected devices that are not in the list.
                // - Remove devices from the current list that are no longer in the sample list.
                // - Add the removed devices to the list of unique devices.

                // Merge the newly sampled list of WiFi devices with the current list of WiFi devices
                // Note that the number of unique devices is EXPECTED to vary throughout the execution
                // but NEVER to exceed the sample size.

                // Remove the devices that have gone out of scope of detection.
                devices_distinct.IntersectWith(devices_sample);

                //if (DEBUG)
                //    Console.WriteLine("Number of WiFi devices in devices_intersect {0}", devices_distinct.Count);

                // Add newly detected devices.
                devices_distinct.UnionWith(devices_sample);

                //if (DEBUG)
                //    Console.WriteLine("Number of WiFi devices in devices_union {0}", devices_distinct.Count);

                // Split the distinct detected WiFi devices by the strength of their power signal into the 
                // "Inside the store Mac Addresses" and "Outside of the store Mac Addresses" and group them 
                // by their respective device vendor type, e.g Apple, Samsung, LG, etc.
                IEnumerable<IGrouping<string, Device>> devices_by_vendor_inside =
                    devices_distinct.Where(x => (x.power > threshold)).GroupBy(x => x.vendor);

                IEnumerable<IGrouping<string, Device>> devices_by_vendor_outside =
                    devices_distinct.Where(x => (x.power <= threshold)).GroupBy(x => x.vendor);


                // Print to a text file and to a standard output via console an updated number of distinct 
                // detected WiFi devices grouped by device vendor/manufacturer that have been collected in 
                // this sample. Since this method is invoked recurrently by the timer's spawned threads put 
                // in place locking mechanism to ensure output file log data integrity and consistency.
                if (!lock_objects.ContainsKey(log_file_uri))
                    lock_objects.Add(log_file_uri, new Object());

                lock (lock_objects[log_file_uri])
                {
                    using (FileStream fs = new FileStream(log_file_uri, FileMode.Append,
                        FileSystemRights.AppendData, FileShare.Read, 4096, FileOptions.None))
                    {
                        using (StreamWriter sw = new StreamWriter(fs))
                        {
                            // Output a list of updated number of devices from the starting time of the application.
                            // Timestamp the output file.
                            sw.WriteLine("Sample time: " + DateTime.Now.ToLocalTime().ToString("hh:mm:ss"));
                            sw.WriteLine("Inside the store:");
                            Console.WriteLine("\nInside the store:");

                            foreach (IGrouping<string, Device> devicegroup in devices_by_vendor_inside)
                            {
                                sw.WriteLine("\t{0}: {1} distinct device{2}", devicegroup.Key,
                                    devicegroup.Count(), (devicegroup.Count() > 1) ? "s" : "");

                                Console.WriteLine("\t{0}: {1} distinct device{2}",
                                    devicegroup.Key, devicegroup.Count(), (devicegroup.Count() > 1) ? "s" : "");

                                //if (DEBUG)
                                //{
                                //    foreach (Device device in devicegroup)
                                //    {
                                //        sw.WriteLine("Mac: " + device.mac + " power: " + device.power);
                                //        Console.WriteLine("Mac: " + device.mac + " power: " + device.power);
                                //    }
                                //}
                            }

                            sw.WriteLine("Outside of the store:");
                            Console.WriteLine("\nOutside of the store:");

                            foreach (IGrouping<string, Device> devicegroup in devices_by_vendor_outside)
                            {
                                sw.WriteLine("\t{0}: {1} distinct device{2}",
                                    devicegroup.Key, devicegroup.Count(), (devicegroup.Count() > 1) ? "s" : "");

                                Console.WriteLine("\t{0}: {1} distinct device{2}", devicegroup.Key,
                                    devicegroup.Count(), (devicegroup.Count() > 1) ? "s" : "");

                                //if (DEBUG)
                                //{
                                //    foreach (Device device in devicegroup)
                                //    {
                                //        sw.WriteLine("Mac: " + device.mac + " power: " + device.power);
                                //        Console.WriteLine("Mac: " + device.mac + " power: " + device.power);
                                //    }
                                //}
                            }

                            sw.WriteLine(" ");
                        }
                    }
                }


                // Keep track of all the unique WiFi devices that have been detected 
                // throughout the execution of the application.
                // Add an extra level of locking protection (not strictly necessary).
                lock (locker)
                {
                    foreach (var device in devices_distinct)
                    {
                        if (!unique_WiFi_devices.ContainsKey(device.mac))
                        {
                            unique_WiFi_devices.Add(device.mac, device);
                            macs_by_sensor_id.Add(device.sensor_id, device.mac);
                        }
                        //else
                        //{
                        //    if (DEBUG)
                        //        Console.WriteLine("Duplicate entry {0}", device.mac);
                        //}
                    }
                }

                Console.Clear();

                //if (DEBUG)
                //    Console.WriteLine("Number of events fired {0}:", num_events_fired);

                //if (DEBUG)
                //    Console.WriteLine("Number of WiFi devices in unique_WiFi_devices {0}", unique_WiFi_devices.Count);

                mtx.ReleaseMutex();

                //if (DEBUG)
                //    Console.WriteLine("{0} has released the mutex\n", Thread.CurrentThread.ManagedThreadId);
            }
            //else
            //{
            //    if (DEBUG)
            //        Console.WriteLine("{0} cannot acquire the mutex", Thread.CurrentThread.ManagedThreadId);
            //}

            // Check to see if it is high time to stop the timer.
            if (num_events_fired >= num_events_to_fire)
            {
                // Signal the StartDevicesRetrieval thread to continue.
                all_done.Set();

                // block_timer = false; // Old way
            }
        }

        // Purpose: To start the timer that spawns threads that collect 
        //          and process batches of the newly detected WiFi devices.
        private static void StartDevicesRetrieval(object obj)
        {
            // block_timer = true;
            num_events_fired = 0;

            Timing timing = (Timing)obj;

            // Create a timer with a specified time interval.
            timer = new System.Timers.Timer(timing.time_interval);

            // Register event to be invoked by the timer at each specified time interval.
            timer.Elapsed += (sender, e) => ProcessDevicesBatch(sender, e, timing.num_events_to_fire, timing.log_file_uri);

            timer.Start();

            // Put this thread to sleep until all the threads that collect and process 
            // batches of the newly detected WiFi devices have finished their respective executions.
            all_done.WaitOne();
        }

        // Purpose: To obtain user input.
        private static int GetUserInput()
        {
            int threshold;

            do
            {
                // Prompt the user to supply an RSSI value (signal strength) threshold
                Console.Write("Please enter a threshold value between [{0}, {1}]: ", RSSI_LOWER_LIMIT, RSSI_UPPER_LIMIT);
                threshold = int.Parse(Console.ReadLine());

                if (threshold < RSSI_LOWER_LIMIT || threshold > RSSI_UPPER_LIMIT)
                    Console.WriteLine("Invalid entry! Retry.");

            } while (threshold < RSSI_LOWER_LIMIT || threshold > RSSI_UPPER_LIMIT);

            return threshold;
        }

        // Purpose: To format WiFi device record.
        private static string[] FormatRecord(string record)
        {
            record = record.Replace("  ", string.Empty).Replace("(hex)",
                string.Empty).Replace("\t", string.Empty).Replace("-", string.Empty);

            string[] parts = record.Split(' ');
            return parts;
        }

        private static void End()
        {
            Console.WriteLine("The application has finished executing.\n" +
                "Log files have been generated and saved in the following directory:\n" +
                LOG_DIR + "\nPress any key to exit...");
        }
    }
}

// A partial list of online resources resorted to in order to implement this little cute application:

// https://www.daniweb.com/software-development/csharp/threads/475686/how-to-retrieve-data-from-url-into-string-json
// http://stackoverflow.com/questions/13297563/read-and-parse-a-json-file-in-c-sharp
// http://www.dotnetperls.com/webclient
// http://msdn.microsoft.com/en-us/library/system.net.webclient%28v=vs.110%29.aspx
// http://www.dotnetperls.com/async
// http://msdn.microsoft.com/en-us/library/hh191443.aspx
// http://json2csharp.com/
// http://james.newtonking.com/json/help/?topic=html/Introduction.htm
// http://stackoverflow.com/questions/2014364/how-do-i-limit-the-number-of-elements-iterated-over-in-a-foreach-loop
// http://msdn.microsoft.com/en-us/library/system.timers.timer.interval(v=vs.110).aspx
// https://www.wireshark.org/tools/oui-lookup.html
// https://github.com/silverwind/oui/blob/master/oui.js
// https://mebsd.com/oui-mac-address-lookup
// http://www.ieee.org/netstorage/standards/oui.txt
// https://svn.nmap.org/nmap/nmap-mac-prefixes
// https://code.wireshark.org/review/gitweb?p=wireshark.git;a=blob_plain;f=manuf
// http://stackoverflow.com/questions/4758283/reading-data-from-a-website-using-c-sharp
// http://www.csharp-station.com/HowTo/HttpWebFetch.aspx
// http://msdn.microsoft.com/en-us/library/aa287535(v=vs.71).aspx
// http://msdn.microsoft.com/en-us/library/ezwyzy7b.aspx
// http://www.dotnetperls.com/dictionary
// http://stackoverflow.com/questions/4598826/c-sharp-hashset-contains-non-unique-objects
// http://blogs.msmvps.com/jonskeet/2011/01/01/reimplementing-linq-to-objects-part-21-groupby/
// http://msdn.microsoft.com/en-us/library/ck8bc5c6.aspx
// http://www.albahari.com/threading/
// http://www.tutorialspoint.com/csharp/csharp_multithreading.htm
// http://www.dotnetperls.com/threadpool
// http://msdn.microsoft.com/en-us/library/system.threading.threadpool(v=vs.110).aspx
// http://www.albahari.com/threading/part3.aspx#_Multithreaded_Timers
// http://www.albahari.com/threading/#_Thread_Pooling
// http://www.albahari.com/threading/part2.aspx#_Synchronization_Contexts
// http://stackoverflow.com/questions/17887407/dictionary-with-list-of-strings-as-value
// http://stackoverflow.com/questions/12570324/c-sharp-run-a-thread-every-x-minutes-but-only-if-that-thread-is-not-running-alr
// https://www.udemy.com/blog/linq-group-by/
// http://msdn.microsoft.com/en-us/library/c5kehkcz.aspx
// http://resources.infosecinstitute.com/multithreading/
// http://msdn.microsoft.com/en-us/library/jj155757.aspx
// http://stackoverflow.com/questions/19902392/writing-to-a-file-from-multiple-threads-without-lock

// https://social.msdn.microsoft.com/Forums/vstudio/en-US/77aaf6e1-8de5-4529-9b26-fa89b55fcc49/best-practices-storing-application-data-and-resources?forum=csharpgeneral
// http://en.wikipedia.org/wiki/User_Account_Control#Requesting%5Felevation
// http://stackoverflow.com/questions/9108399/how-to-grant-full-permission-to-a-file-created-by-my-application-for-all-users
// http://stackoverflow.com/questions/946420/allow-access-permission-to-write-in-program-files-of-windows-7
// http://stackoverflow.com/questions/8944765/c-sharp-set-directory-permissions-for-all-users-in-windows-7
// http://msdn.microsoft.com/en-us/library/system.io.directory.setaccesscontrol(v=vs.110).aspx
// http://msdn.microsoft.com/en-us/library/54a0at6s(v=vs.110).aspx


//private const string LOG_FILE_URI_DEVICES_DISTINCT = @"..\..\Logs\DistinctWiFiDevicesAsIs_REC.txt";
//private const string LOG_DIR = @"\Analoc\Homework\Logs";
//private const string LOG_FILE_URI_DEVICES_DISTINCT = LOG_DIR + "\\DistinctWiFiDevicesAsIs_REC.txt";
//private const string LOG_FILE_URI_AS_IS_SEQUENTIAL = LOG_DIR + "\\DistinctWiFiDevicesAsIs_SEQ.txt";
//private const string LOG_FILE_URI_GROUPED_SEQUENTIAL = LOG_DIR + @"\DistinctWiFiDevicesGroupedBySensorID_SEQ.txt";
//private const string LOG_FILE_URI_GROUPED_MULTITHREADING = LOG_DIR + @"\DistinctWiFiDevicesGroupedBySensorID_MUL.txt";


// Your program should not write temporary files (or anything else for that matter) to the program directory. 
// Any program should use %TEMP% for temporary files and %APPDATA% for user specific application data. 
// This has been true since Windows 2000/XP so you should change your application.

// Your program has to run with Administrative Rights. 
// You can't do this automatically with code, but you can request the user (in code) to elevate the rights 
// of your program while it's running. There's a wiki on how to do this. 
// Alternatively, any program can be run as administrator by right-clicking its icon and clicking 
// "Run as administrator".
// However, I wouldn't suggest doing this. It would be better to use something like this:

// Environment.GetFolderPath(SpecialFolder.ApplicationData);

// Environment.GetFolderPath(SpecialFolder.CommonApplicationData);

// to get the AppData Folder path and create a folder there for your app. Then put the temp files there.

// Directory.CreateDirectory(LOG_DIR);
// Directory.CreateDirectory(LOG_DIR, dSecurity);
// DirectoryInfo dInfo = new DirectoryInfo(LOG_DIR);
// Directory.SetAccessControl(LOG_DIR, dSecurity);

// The directory that serves as a common repository for application-specific data for the current roaming user.
// string ApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

// User-specific data
// string LocalApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);