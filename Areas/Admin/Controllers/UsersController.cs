using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DataManager _dataManager;
        private readonly AppDbContext _context;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            DataManager dataManager,
            AppDbContext context)
        {
            _userManager = userManager;
            _dataManager = dataManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users
                .OrderBy(x => x.UserName)
                .ToListAsync();

            return View(users);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string? id)
        {
            var model = await BuildEditModelAsync();

            if (string.IsNullOrWhiteSpace(id))
                return View(model);

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var facilityIds = await _context.UserFacilities
                .Where(x => x.UserId == user.Id)
                .Select(x => x.FacilityId)
                .ToListAsync();

            model.Id = new Guid(user.Id);
            model.UserName = user.UserName ?? "";
            model.Email = user.Email ?? "";
            model.Role = roles.FirstOrDefault() ?? "operator";
            model.SelectedFacilityIds = facilityIds;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserEditViewModel model)
        {
            model = await RebuildListsAsync(model);

            if (!ModelState.IsValid)
                return View(model);
             
            if (model.Id == Guid.Empty)
            {
                var existingUser = await _userManager.FindByNameAsync(model.UserName.Trim());
                if (existingUser != null)
                {
                    ModelState.AddModelError(nameof(model.UserName), "Користувач з таким логіном уже існує");
                    return View(model);
                }

                var existingEmail = await _userManager.FindByEmailAsync(model.Email.Trim());
                if (existingEmail != null)
                {
                    ModelState.AddModelError(nameof(model.Email), "Користувач з таким email уже існує");
                    return View(model);
                }

                if (string.IsNullOrWhiteSpace(model.Password))
                {
                    ModelState.AddModelError(nameof(model.Password), "Для нового користувача потрібно вказати пароль");
                    return View(model);
                }

                var user = new ApplicationUser
                {
                    UserName = model.UserName.Trim(),
                    Email = model.Email.Trim(),
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    foreach (var error in createResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    return View(model);
                }

                var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
                if (!roleResult.Succeeded)
                {
                    foreach (var error in roleResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    await _userManager.DeleteAsync(user);
                    return View(model);
                }

                if (model.SelectedFacilityIds.Any())
                {
                    foreach (var facilityId in model.SelectedFacilityIds.Distinct())
                    {
                        _context.UserFacilities.Add(new UserFacility
                        {
                            UserId = user.Id,
                            FacilityId = facilityId
                        });
                    }

                    await _context.SaveChangesAsync();
                }

                return RedirectToAction(nameof(Index));
            }

            var existing = await _userManager.FindByIdAsync(model.Id.ToString()!);
            if (existing == null)
                return NotFound();

            var userByName = await _userManager.FindByNameAsync(model.UserName.Trim());
            if (userByName != null && userByName.Id != existing.Id)
            {
                ModelState.AddModelError(nameof(model.UserName), "Користувач з таким логіном уже існує");
                return View(model);
            }

            var userByEmail = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (userByEmail != null && userByEmail.Id != existing.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "Користувач з таким email уже існує");
                return View(model);
            }

            existing.UserName = model.UserName.Trim();
            existing.Email = model.Email.Trim();
            existing.NormalizedUserName = _userManager.NormalizeName(existing.UserName);
            existing.NormalizedEmail = _userManager.NormalizeEmail(existing.Email);

            var updateResult = await _userManager.UpdateAsync(existing);
            if (!updateResult.Succeeded)
            {
                foreach (var error in updateResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }

            if (!string.IsNullOrWhiteSpace(model.Password))
            {
                var removePasswordResult = await RemovePasswordIfExistsAsync(existing);
                if (!removePasswordResult.Succeeded)
                {
                    foreach (var error in removePasswordResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    return View(model);
                }

                var addPasswordResult = await _userManager.AddPasswordAsync(existing, model.Password);
                if (!addPasswordResult.Succeeded)
                {
                    foreach (var error in addPasswordResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    return View(model);
                }
            }

            var currentRoles = await _userManager.GetRolesAsync(existing);
            if (currentRoles.Count > 0)
            {
                var removeRolesResult = await _userManager.RemoveFromRolesAsync(existing, currentRoles);
                if (!removeRolesResult.Succeeded)
                {
                    foreach (var error in removeRolesResult.Errors)
                        ModelState.AddModelError(string.Empty, error.Description);

                    return View(model);
                }
            }

            var addRoleResult = await _userManager.AddToRoleAsync(existing, model.Role);
            if (!addRoleResult.Succeeded)
            {
                foreach (var error in addRoleResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return View(model);
            }

            var oldLinks = await _context.UserFacilities
                .Where(x => x.UserId == existing.Id)
                .ToListAsync();

            if (oldLinks.Count > 0)
                _context.UserFacilities.RemoveRange(oldLinks);

            if (model.SelectedFacilityIds.Any())
            {
                foreach (var facilityId in model.SelectedFacilityIds.Distinct())
                {
                    _context.UserFacilities.Add(new UserFacility
                    {
                        UserId = existing.Id,
                        FacilityId = facilityId
                    });
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<UserEditViewModel> BuildEditModelAsync()
        {
            return new UserEditViewModel
            {
                Facilities = await _dataManager.Facilities.GetFacilities()
                    .OrderBy(f => f.Name)
                    .Select(f => new SelectListItem
                    {
                        Value = f.Id.ToString(),
                        Text = f.Name
                    })
                    .ToListAsync(),
                Roles = new List<SelectListItem>
                {
                    new SelectListItem { Value = "operator", Text = "operator" },
                    new SelectListItem { Value = "admin", Text = "admin" }
                }
            };
        }

        private async Task<UserEditViewModel> RebuildListsAsync(UserEditViewModel model)
        {
            var baseModel = await BuildEditModelAsync();

            model.Facilities = baseModel.Facilities;
            model.Roles = baseModel.Roles;

            return model;
        }

        private async Task<IdentityResult> RemovePasswordIfExistsAsync(ApplicationUser user)
        {
            var hasPassword = await _userManager.HasPasswordAsync(user);
            if (!hasPassword)
                return IdentityResult.Success;

            return await _userManager.RemovePasswordAsync(user);
        }
    }
}