using System.ComponentModel.DataAnnotations;

namespace SumyCRM.Models
{
    public abstract class EntityBase
    {
        protected EntityBase() => DateAdded = DateTime.UtcNow;
        [Required]
        public Guid Id { get; set; }
        [Display(Name = "Приховати")]
        public virtual bool Hidden { get; set; }
        [DataType(DataType.Time)]
        public DateTime DateAdded { get; set; }
    }
}
