namespace RainWorldConnect.Attributes {
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class SerializableMemberAttribute : Attribute {
        public int Index {
            get; set;
        } = 0;
        public bool SkipNullCheck {
            get; set;
        } = false;
    }
}
