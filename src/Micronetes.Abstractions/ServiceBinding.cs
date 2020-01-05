namespace Micronetes
{
    public readonly struct ServiceBinding
    {
        public string Address { get; }
        public string Protocol { get; }

        public ServiceBinding(string address, string protocol)
        {
            Address = address;
            Protocol = protocol;
        }
    }
}
