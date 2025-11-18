namespace AppISO.Entities
{
    public class User
    {
        public int Id { get; set; }

        public string UserName { get; set; } = default!;

        public string PasswordHash { get; set; } = default!;

        public string Role { get; set; } = "Agent";

        public bool IsActive { get; set; } = true;

        public int FailedLoginAttemps { get; set; }

        public DateTime? LockTimeEnd { get; set; }

        public DateTime DateCreated { get; set; }
    }
}
