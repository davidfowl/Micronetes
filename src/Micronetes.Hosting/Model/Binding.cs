namespace Micronetes.Hosting
{
    public class Binding
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Protocol { get; set; }

        internal bool IsDefault => Name == "default";
    }
}
