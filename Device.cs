
namespace Homework
{
    public class Device
    {
        public Device()
        {
            this.vendor = "Unrecognized";
        }

        public Device(string mac, int power, int sensor_id, string timestamp, string vendor = "Unrecognized")
        {
            this.mac = mac;
            this.power = power;
            this.sensor_id = sensor_id;
            this.timestamp = timestamp;
            this.vendor = vendor;
        }

        public override int GetHashCode()
        {
            unchecked // Wrap in case of overflow
            {
                int hash = 17;
                hash = hash * 23 + this.mac.GetHashCode();
                hash = hash * 23 + this.power.GetHashCode();
                hash = hash * 23 + this.sensor_id.GetHashCode();
                hash = hash * 23 + this.timestamp.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as Device;

            return other != null && this.mac == other.mac;
        }

        public string mac { get; set; }         // Media Access Control (MAC) address of the device
        public int power { get; set; }          // Received Signal Strength Indication (RSSI) value (negative, lower is weaker)
        public int sensor_id { get; set; }      // Identification number of the sensor that picked up the signal
        public string timestamp { get; set; }   // Date and time when the device was last detected
        public string vendor { get; set; }      // Vendor name that manufactured this device
    }
}