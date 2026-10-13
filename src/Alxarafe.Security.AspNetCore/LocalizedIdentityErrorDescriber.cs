using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Security.AspNetCore;

/// <summary>Native Identity error descriptions; codes and policies remain framework-owned.</summary>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SecurityMessages> localizer) : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Describe(base.DefaultError());
    public override IdentityError ConcurrencyFailure() => Describe(base.ConcurrencyFailure());
    public override IdentityError PasswordMismatch() => Describe(base.PasswordMismatch());
    public override IdentityError InvalidToken() => Describe(base.InvalidToken());
    public override IdentityError RecoveryCodeRedemptionFailed() => Describe(base.RecoveryCodeRedemptionFailed());
    public override IdentityError LoginAlreadyAssociated() => Describe(base.LoginAlreadyAssociated());
    public override IdentityError InvalidUserName(string? userName) => Describe(base.InvalidUserName(userName), userName);
    public override IdentityError InvalidEmail(string? email) => Describe(base.InvalidEmail(email), email);
    public override IdentityError DuplicateUserName(string userName) => Describe(base.DuplicateUserName(userName), userName);
    public override IdentityError DuplicateEmail(string email) => Describe(base.DuplicateEmail(email), email);
    public override IdentityError InvalidRoleName(string? role) => Describe(base.InvalidRoleName(role), role);
    public override IdentityError DuplicateRoleName(string role) => Describe(base.DuplicateRoleName(role), role);
    public override IdentityError UserAlreadyHasPassword() => Describe(base.UserAlreadyHasPassword());
    public override IdentityError UserLockoutNotEnabled() => Describe(base.UserLockoutNotEnabled());
    public override IdentityError UserAlreadyInRole(string role) => Describe(base.UserAlreadyInRole(role), role);
    public override IdentityError UserNotInRole(string role) => Describe(base.UserNotInRole(role), role);
    public override IdentityError PasswordTooShort(int length) => Describe(base.PasswordTooShort(length), length);
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Describe(base.PasswordRequiresUniqueChars(uniqueChars), uniqueChars);
    public override IdentityError PasswordRequiresNonAlphanumeric() => Describe(base.PasswordRequiresNonAlphanumeric());
    public override IdentityError PasswordRequiresDigit() => Describe(base.PasswordRequiresDigit());
    public override IdentityError PasswordRequiresLower() => Describe(base.PasswordRequiresLower());
    public override IdentityError PasswordRequiresUpper() => Describe(base.PasswordRequiresUpper());

    private IdentityError Describe(IdentityError error, params object?[] arguments)
    {
        error.Description = localizer[SecurityMessages.IdentityPrefix + error.Code, arguments!].Value;
        return error;
    }
}
