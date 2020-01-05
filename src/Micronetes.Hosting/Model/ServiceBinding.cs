namespace Micronetes.Hosting.Model
{
    public class ServiceBinding
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Protocol { get; set; }

        internal bool IsDefault => Name == "default";
    }
}
