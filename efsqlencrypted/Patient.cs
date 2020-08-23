using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EfSqlEncrypted
{
    public class Patient
    {

        public int PatientId { get; set; }
        [Column(TypeName = "varchar(255)")]
        public string Email { get; set; }

        [Column(TypeName = "varchar(20)")]
        public string SSN { get; set; }

        [Column(TypeName = "varchar(255)")]
        public string FirstName { get; set; }

        [Column(TypeName = "varchar(255)")]
        public string LastName { get; set; }

        public DateTime BirthDate { get; set; }
    }
}
