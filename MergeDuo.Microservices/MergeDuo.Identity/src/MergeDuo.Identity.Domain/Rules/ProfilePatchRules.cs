using MergeDuo.Identity.Domain.Documents;

namespace MergeDuo.Identity.Domain.Rules;

public static class ProfilePatchRules
{
    public static IReadOnlyList<string> Validate(string? name, string? handle, string? phone)
    {
        var errors = new List<string>();
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name");
        }

        if (handle is not null && !HandleRules.IsValid(HandleRules.Normalize(handle)))
        {
            errors.Add("handle");
        }

        if (phone is not null && phone.Length > 40)
        {
            errors.Add("phone");
        }

        return errors;
    }

    public static void Apply(
        UserDocument user,
        string? name,
        string? handle,
        string? phone,
        UserPreferences? preferences,
        DateTimeOffset now)
    {
        if (name is not null)
        {
            user.Name = name.Trim();
            user.AvatarInitials = IdentityRules.Initials(user.Name, user.Email);
        }

        if (handle is not null)
        {
            user.Handle = HandleRules.Normalize(handle);
        }

        if (phone is not null)
        {
            user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        }

        if (preferences is not null)
        {
            user.Preferences = preferences;
        }

        user.UpdatedAt = now;
    }
}
