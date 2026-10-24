using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Constants;
using KitchenInventory.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class AdminSetupViewModelTests
{
    private sealed class FakeAuth : IAuthenticationService
    {
        public bool CreateResult { get; set; } = true;
        public string? LastRole { get; private set; }

        public Task<bool> CreateUserAsync(string username, string password, string role, CancellationToken ct = default)
        {
            LastRole = role;
            return Task.FromResult(CreateResult);
        }

        public Task<(bool Success, string? Error)> SignInAsync(string username, string password, CancellationToken ct = default)
            => Task.FromResult((false, (string?)null));

        public Task SignOutAsync() => Task.CompletedTask;
        public Task<bool> HasAnyUsersAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<User>)Array.Empty<User>());
        public Task<(bool Success, string? Error)> UpdateUserRoleAsync(int userId, string newRole, string actingUsername, CancellationToken ct = default)
            => Task.FromResult((true, (string?)null));
        public Task<(bool Success, string? Error)> ResetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
            => Task.FromResult((true, (string?)null));
        public Task<(bool Success, string? Error)> DeleteUserAsync(int userId, string actingUsername, CancellationToken ct = default)
            => Task.FromResult((true, (string?)null));
        public Task<(bool Success, string? Error)> UnlockUserAsync(int userId, CancellationToken ct = default)
            => Task.FromResult((true, (string?)null));
    }

    private static AdminSetupViewModel Create(FakeAuth? auth = null, ISessionService? session = null)
        => new(auth ?? new FakeAuth(), session ?? new SessionService(), NullLogger<AdminSetupViewModel>.Instance);

    [Fact]
    public void CanSubmit_False_WhenFieldsEmpty()
    {
        var vm = Create();

        vm.CanSubmit.Should().BeFalse();
        vm.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void CanSubmit_True_WhenFieldsValidAndMatching()
    {
        var vm = Create();

        vm.Username = "admin";
        vm.Password = "P@ssw0rd!";
        vm.ConfirmPassword = "P@ssw0rd!";

        vm.CanSubmit.Should().BeTrue();
        vm.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void SetProperties_RaisePropertyChangedAndErrorsChanged()
    {
        var vm = Create();
        var props = new List<string?>();
        var errorProps = new List<string?>();
        vm.PropertyChanged += (_, e) => props.Add(e.PropertyName);
        vm.ErrorsChanged += (_, e) => errorProps.Add(e.PropertyName);

        vm.Username = "admin";

        props.Should().Contain(nameof(vm.Username));
        errorProps.Should().Contain(nameof(vm.Username))
            .And.Contain(nameof(vm.Password))
            .And.Contain(nameof(vm.ConfirmPassword));
    }

    [Fact]
    public void GetErrors_ReportsMissingAndMismatchedFields()
    {
        var vm = Create();
        vm.Username = "admin";
        vm.Password = "a";
        vm.ConfirmPassword = "b";

        vm.GetErrors(nameof(vm.Username)).Cast<string>().Should().BeEmpty();
        vm.GetErrors(nameof(vm.ConfirmPassword)).Cast<string>().Should().Contain("Passwords must match");
    }

    [Fact]
    public async Task TryCreateAdmin_EmptyFields_SetsRequiredError()
    {
        var vm = Create();

        var ok = await vm.TryCreateAdminAsync();

        ok.Should().BeFalse();
        vm.Error.Should().Be("All fields are required");
    }

    [Fact]
    public async Task TryCreateAdmin_PasswordMismatch_SetsMismatchError()
    {
        var vm = Create();
        vm.Username = "admin";
        vm.Password = "a";
        vm.ConfirmPassword = "b";

        var ok = await vm.TryCreateAdminAsync();

        ok.Should().BeFalse();
        vm.Error.Should().Be("Passwords do not match");
    }

    [Fact]
    public async Task TryCreateAdmin_ExistingUser_SetsExistsError()
    {
        var auth = new FakeAuth { CreateResult = false };
        var vm = Create(auth);
        vm.Username = "admin";
        vm.Password = "P@ssw0rd!";
        vm.ConfirmPassword = "P@ssw0rd!";

        var ok = await vm.TryCreateAdminAsync();

        ok.Should().BeFalse();
        vm.Error.Should().Be("User already exists");
    }

    [Fact]
    public async Task TryCreateAdmin_Success_SignsInAsAdmin()
    {
        var auth = new FakeAuth();
        var session = new SessionService();
        var vm = Create(auth, session);
        vm.Username = "admin";
        vm.Password = "P@ssw0rd!";
        vm.ConfirmPassword = "P@ssw0rd!";

        var ok = await vm.TryCreateAdminAsync();

        ok.Should().BeTrue();
        vm.Error.Should().BeNull();
        auth.LastRole.Should().Be(Roles.Admin);
        session.IsAuthenticated.Should().BeTrue();
        session.CurrentUsername.Should().Be("admin");
        session.CurrentRole.Should().Be(Roles.Admin);
    }
}
