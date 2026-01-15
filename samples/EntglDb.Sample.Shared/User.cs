using System.ComponentModel.DataAnnotations;
using EntglDb.Core.Metadata;

namespace EntglDb.Sample.Shared
{
    public class User
    {
        [Key]
        public string Id { get; set; } = "";
        
        public string? Name { get; set; }
        
        [Indexed]
        public int Age { get; set; }
        
        public Address? Address { get; set; }
    }

    public class Address
    {
        public string? City { get; set; }
    }
}
