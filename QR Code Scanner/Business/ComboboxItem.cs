namespace QR_Code_Scanner.Business
{
    public class ComboboxItem
    {
        public string Name;
        public string ID;
        public FrameSourceInformation MediaFrameSourceInformation;
        public ComboboxItem(string name, string id, FrameSourceInformation mediaFrameSourceInformation)
        {
            Name = name; ID = id; MediaFrameSourceInformation = mediaFrameSourceInformation;
        }
        public override string ToString()
        {
            return Name;
        }
    }
}
