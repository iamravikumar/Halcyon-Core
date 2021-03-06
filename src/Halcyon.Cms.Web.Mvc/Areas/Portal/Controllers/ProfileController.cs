﻿// Licensed to the Halcyon Core Foundation under one or more agreements.
// The Halcyon Core Foundation licenses this file to you under the GNU General Public License v3.0.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Halcyon.Identity.Models;
using Halcyon.Identity.Models.ManageViewModels;
using Halcyon.Identity.Services;
using System.Linq;
using System.Threading.Tasks;

namespace Halcyon.Cms.Mvc.Areas.Portal.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    [Area("Portal")]
    //[Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly ILogger _logger;

        public ProfileController(
          UserManager<ApplicationUser> userManager,
          SignInManager<ApplicationUser> signInManager,
          IEmailSender emailSender,
          ISmsSender smsSender,
          ILoggerFactory loggerFactory)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _logger = loggerFactory.CreateLogger<ProfileController>();
        }

        //
        // GET: /Manage/Index
        [HttpGet]
        public async Task<IActionResult> Index(ManageMessageId? message = null)
        {
            ViewData["StatusMessage"] =
                message == ManageMessageId.ChangePasswordSuccess ? "Your password has been changed."
                : message == ManageMessageId.SetPasswordSuccess ? "Your password has been set."
                : message == ManageMessageId.SetTwoFactorSuccess ? "Your two-factor authentication provider has been set."
                : message == ManageMessageId.Error ? "An error has occurred."
                : message == ManageMessageId.AddPhoneSuccess ? "Your phone number was added."
                : message == ManageMessageId.RemovePhoneSuccess ? "Your phone number was removed."
                : "";

            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user == null)
            {
                return View("Error");
            }
            var model = new IndexViewModel
            {
                HasPassword = await _userManager.HasPasswordAsync(user).ConfigureAwait(false),
                PhoneNumber = await _userManager.GetPhoneNumberAsync(user).ConfigureAwait(false),
                TwoFactor = await _userManager.GetTwoFactorEnabledAsync(user).ConfigureAwait(false),
                Logins = await _userManager.GetLoginsAsync(user).ConfigureAwait(false),
                BrowserRemembered = await _signInManager.IsTwoFactorClientRememberedAsync(user).ConfigureAwait(false)
            };
            return View(model);
        }

        //
        // POST: /Manage/RemoveLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveLogin(RemoveLoginViewModel account)
        {
            ManageMessageId? message = ManageMessageId.Error;
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                var result = await _userManager.RemoveLoginAsync(user, account.LoginProvider, account.ProviderKey).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                    message = ManageMessageId.RemoveLoginSuccess;
                }
            }
            return RedirectToAction(nameof(ManageLogins), new { Message = message });
        }

        //
        // GET: /Manage/AddPhoneNumber
        public IActionResult AddPhoneNumber()
        {
            return View();
        }

        //
        // POST: /Manage/AddPhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPhoneNumber(AddPhoneNumberViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            // Generate the token and send it
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user == null)
            {
                return View("Error");
            }
            var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, model.PhoneNumber).ConfigureAwait(false);
            await _smsSender.SendSmsAsync(model.PhoneNumber, "Your security code is: " + code).ConfigureAwait(false);
            return RedirectToAction(nameof(VerifyPhoneNumber), new { model.PhoneNumber });
        }

        //
        // POST: /Manage/EnableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnableTwoFactorAuthentication()
        {
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
                await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                _logger.LogInformation(1, "User enabled two-factor authentication.");
            }
            return RedirectToAction(nameof(Index), "Manage");
        }

        //
        // POST: /Manage/DisableTwoFactorAuthentication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableTwoFactorAuthentication()
        {
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                await _userManager.SetTwoFactorEnabledAsync(user, false).ConfigureAwait(false);
                await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                _logger.LogInformation(2, "User disabled two-factor authentication.");
            }
            return RedirectToAction(nameof(Index), "Manage");
        }

        //
        // GET: /Manage/VerifyPhoneNumber
        [HttpGet]
        public async Task<IActionResult> VerifyPhoneNumber(string phoneNumber)
        {
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user == null)
            {
                return View("Error");
            }
            var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber).ConfigureAwait(false);
            // Send an SMS to verify the phone number
            return phoneNumber == null ? View("Error") : View(new VerifyPhoneNumberViewModel { PhoneNumber = phoneNumber });
        }

        //
        // POST: /Manage/VerifyPhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyPhoneNumber(VerifyPhoneNumberViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                var result = await _userManager.ChangePhoneNumberAsync(user, model.PhoneNumber, model.Code).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                    return RedirectToAction(nameof(Index), new { Message = ManageMessageId.AddPhoneSuccess });
                }
            }
            // If we got this far, something failed, redisplay the form
            ModelState.AddModelError(string.Empty, "Failed to verify phone number");
            return View(model);
        }

        //
        // POST: /Manage/RemovePhoneNumber
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePhoneNumber()
        {
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                var result = await _userManager.SetPhoneNumberAsync(user, null).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                    return RedirectToAction(nameof(Index), new { Message = ManageMessageId.RemovePhoneSuccess });
                }
            }
            return RedirectToAction(nameof(Index), new { Message = ManageMessageId.Error });
        }

        //
        // GET: /Manage/ChangePassword
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        //
        // POST: /Manage/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                    _logger.LogInformation(3, "User changed their password successfully.");
                    return RedirectToAction(nameof(Index), new { Message = ManageMessageId.ChangePasswordSuccess });
                }
                AddErrors(result);
                return View(model);
            }
            return RedirectToAction(nameof(Index), new { Message = ManageMessageId.Error });
        }

        //
        // GET: /Manage/SetPassword
        [HttpGet]
        public IActionResult SetPassword()
        {
            return View();
        }

        //
        // POST: /Manage/SetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user != null)
            {
                var result = await _userManager.AddPasswordAsync(user, model.NewPassword).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
                    return RedirectToAction(nameof(Index), new { Message = ManageMessageId.SetPasswordSuccess });
                }
                AddErrors(result);
                return View(model);
            }
            return RedirectToAction(nameof(Index), new { Message = ManageMessageId.Error });
        }

        //GET: /Manage/ManageLogins
        [HttpGet]
        public async Task<IActionResult> ManageLogins(ManageMessageId? message = null)
        {
            ViewData["StatusMessage"] =
                message == ManageMessageId.RemoveLoginSuccess ? "The external login was removed."
                : message == ManageMessageId.AddLoginSuccess ? "The external login was added."
                : message == ManageMessageId.Error ? "An error has occurred."
                : "";
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user == null)
            {
                return View("Error");
            }
            var userLogins = await _userManager.GetLoginsAsync(user).ConfigureAwait(false);
            var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync().ConfigureAwait(false);
            var otherLogins = schemes.Where(auth => userLogins.All(ul => auth.Name != ul.LoginProvider)).ToList();
            ViewData["ShowRemoveButton"] = user.PasswordHash != null || userLogins.Count > 1;
            return View(new ManageLoginsViewModel
            {
                CurrentLogins = userLogins,
                OtherLogins = otherLogins
            });
        }

        //
        // POST: /Manage/LinkLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LinkLogin(string provider)
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);

            // Request a redirect to the external login provider to link a login for the current user
            var redirectUrl = Url.Action(nameof(LinkLoginCallback), "Manage");
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _userManager.GetUserId(User));
            return Challenge(properties, provider);
        }

        //
        // GET: /Manage/LinkLoginCallback
        [HttpGet]
        public async Task<ActionResult> LinkLoginCallback()
        {
            var user = await GetCurrentUserAsync().ConfigureAwait(false);
            if (user == null)
            {
                return View("Error");
            }
            var info = await _signInManager.GetExternalLoginInfoAsync(await _userManager.GetUserIdAsync(user).ConfigureAwait(false)).ConfigureAwait(false);
            if (info == null)
            {
                return RedirectToAction(nameof(ManageLogins), new { Message = ManageMessageId.Error });
            }
            var result = await _userManager.AddLoginAsync(user, info).ConfigureAwait(false);
            var message = ManageMessageId.Error;
            if (result.Succeeded)
            {
                message = ManageMessageId.AddLoginSuccess;
                // Clear the existing external cookie to ensure a clean login process
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme).ConfigureAwait(false);
            }
            return RedirectToAction(nameof(ManageLogins), new { Message = message });
        }

        #region Helpers

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        public enum ManageMessageId
        {
            AddPhoneSuccess,
            AddLoginSuccess,
            ChangePasswordSuccess,
            SetTwoFactorSuccess,
            SetPasswordSuccess,
            RemoveLoginSuccess,
            RemovePhoneSuccess,
            Error
        }

        private Task<ApplicationUser> GetCurrentUserAsync()
        {
            return _userManager.GetUserAsync(HttpContext.User);
        }

        #endregion Helpers
    }
}
