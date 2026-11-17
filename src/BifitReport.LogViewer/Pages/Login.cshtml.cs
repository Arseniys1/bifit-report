using System.ComponentModel.DataAnnotations;
using BifitReport.LogViewer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace BifitReport.LogViewer.Pages;

[AllowAnonymous]
[EnableRateLimiting("login")]
public sealed class LoginModel(
    SignInManager<LogViewerUser> signInManager,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(ReturnUrl);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var result = await signInManager.PasswordSignInAsync(
            Input.UserName,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);
        Input.Password = string.Empty;

        if (result.Succeeded)
        {
            logger.LogInformation("Пользователь {UserName} вошёл в Log Viewer.", Input.UserName);
            return RedirectToLocal(ReturnUrl);
        }
        if (result.IsLockedOut)
        {
            logger.LogWarning("Учётная запись {UserName} временно заблокирована после неудачных попыток входа.", Input.UserName);
            ModelState.AddModelError(string.Empty, "Слишком много неудачных попыток. Повторите через 15 минут.");
            return Page();
        }

        logger.LogWarning("Неудачная попытка входа для {UserName} с адреса {RemoteAddress}.", Input.UserName, HttpContext.Connection.RemoteIpAddress);
        ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
        return Page();
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : LocalRedirect("/");

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Введите логин.")]
        [StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите пароль.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
