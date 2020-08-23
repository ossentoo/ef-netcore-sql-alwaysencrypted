using System;

namespace EfSqlEncrypted
{
    public class Patient
    {

        public int PatientId { get; set; }
        public string Email { get; set; }
        public string SSN { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime BirthDate { get; set; }
    }
}
