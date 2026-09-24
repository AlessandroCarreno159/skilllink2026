using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.ViewModels;
using System.Text.RegularExpressions;

namespace SkillLink_dotnet.Controllers;

/// <summary>
/// Registro / login por DNI + logout. Replica blueprints/auth.py.
/// </summary>
public class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : Controller
{
    [HttpGet] public IActionResult Register() => View(new RegisterVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!Regex.IsMatch(vm.Dni ?? "", @"^\d{8}$"))
            ModelState.AddModelError(nameof(vm.Dni), "El DNI debe tener 8 dígitos numéricos.");
        if (await db.BaneosDni.AnyAsync(b => b.Dni == vm.Dni))
            ModelState.AddModelError(nameof(vm.Dni), "No puede registrarse con este DNI.");
        if (!ModelState.IsValid) return View(vm);

        var rol = vm.Rol is Roles.Empresa or Roles.Trabajador ? vm.Rol : Roles.Trabajador;
        var user = new ApplicationUser
        {
            UserName = vm.Dni!, Dni = vm.Dni!, Email = vm.Email!,
            Rol = rol, EstadoCuenta = EstadosCuenta.Incompleto
        };
        var result = await users.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }
        await users.AddToRoleAsync(user, rol);
        TempData["Success"] = "Cuenta creada. Complete su perfil para enviar la solicitud de aprobación.";
        return rol == Roles.Empresa
            ? RedirectToAction("Register", "Company", new { userId = user.Id })
            : RedirectToAction("Register", "Worker", new { userId = user.Id });
    }

    [HttpGet] public IActionResult Login() => View(new LoginVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        if (await db.BaneosDni.AnyAsync(b => b.Dni == vm.Dni))
        { ModelState.AddModelError("", "Este DNI ha sido baneado permanentemente de SkillLink."); return View(vm); }

        var user = await users.FindByNameAsync(vm.Dni);
        if (user is null)
        { ModelState.AddModelError("", "DNI o contraseña incorrectos."); return View(vm); }
        var check = await signIn.CheckPasswordSignInAsync(user, vm.Password, lockoutOnFailure: false);
        if (!check.Succeeded)
        { ModelState.AddModelError("", "DNI o contraseña incorrectos."); return View(vm); }
        if (user.EstadoCuenta == EstadosCuenta.Baneado)
        { ModelState.AddModelError("", "Su cuenta está baneada permanentemente."); return View(vm); }

        await signIn.SignInAsync(user, vm.RememberMe);
        user.UltimoAcceso = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (user.EstadoCuenta == EstadosCuenta.Incompleto)
        {
            TempData["Info"] = "Debe completar su perfil antes de solicitar la aprobación.";
            return user.Rol == Roles.Empresa
                ? RedirectToAction("Register", "Company", new { userId = user.Id })
                : RedirectToAction("Register", "Worker", new { userId = user.Id });
        }
        TempData["Success"] = "Bienvenido a SkillLink.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        TempData["Info"] = "Sesión cerrada.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet] public IActionResult AccessDenied() => View();
}
