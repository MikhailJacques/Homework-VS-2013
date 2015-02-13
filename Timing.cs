namespace Homework
{
    class Timing
    {
        public Timing(int time_interval, int num_events_to_fire, string log_file_uri)
        {
            this.time_interval = time_interval;
            this.num_events_to_fire = num_events_to_fire;
            this.log_file_uri = log_file_uri;
        }

        public int time_interval { get; set; }
        public int num_events_to_fire { get; set; }
        public string log_file_uri { get; set; }
    }
}