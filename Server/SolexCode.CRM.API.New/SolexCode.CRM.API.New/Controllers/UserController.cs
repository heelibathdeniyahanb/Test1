﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolexCode.CRM.API.New.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Threading.Tasks;
using System;
using SolexCode.CRM.API.New.Data;
using static System.Net.WebRequestMethods;
using SolexCode.CRM.API.New.Dtos;

namespace SolexCode.CRM.API.New.Controllers
{
    [Route("api/user/[action]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly DatabaseContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly EmailController _emailController;
        private readonly IConfiguration _configuration;

        public UserController(DatabaseContext context, EmailController emailController, IWebHostEnvironment environment, IConfiguration configuration)
        {
            _context = context;
            _environment = environment;
            _configuration = configuration;
            _emailController = emailController;
        }

        // Generate Temporary Password
        private string GenerateTemporaryPassword(int length = 12)
        {
            const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*?_-";
            var random = new Random();
            return new string(Enumerable.Repeat(validChars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // Generate OTP and send to user's email
        [HttpPost("GenerateOtp")]
        public async Task<IActionResult> GenerateOtp([FromBody] EmailRequest emailRequest)
        {
            string email = emailRequest.Email;

            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email is required.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var otpCode = new Random().Next(100000, 999999).ToString(); // Generate a 6-digit OTP
            var otp = new Otp
            {
                UserId = user.Id,
                Code = otpCode,
                GeneratedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(10), // OTP valid for 10 minutes
                IsVerified = false
            };

            _context.Otps.Add(otp);
            await _context.SaveChangesAsync();

            // Send OTP to user's email
            await _emailController.SendOtp(user.Email, otpCode);

            return Ok("OTP sent to email.");
        }


        // Verify OTP
        [HttpPost("VerifyOtp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerificationRequest otpVerificationRequest)
        {
            string email = otpVerificationRequest.Email;
            string otpCode = otpVerificationRequest.OtpCode;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var otp = await _context.Otps
                .Where(o => o.UserId == user.Id && o.Code == otpCode && o.ExpiresAt > DateTime.UtcNow && !o.IsVerified)
                .FirstOrDefaultAsync();

            if (otp == null)
            {
                return BadRequest("Invalid or expired OTP.");
            }

            otp.IsVerified = true;
            _context.Otps.Update(otp);
            await _context.SaveChangesAsync();

            // Return the userId along with the message
            return Ok(new { message = "OTP verified.", userId = user.Id });
        }

        //Email Request
        public class EmailRequest
        {
            public string Email { get; set; }
        }
        // Otp Verification Request
        public class OtpVerificationRequest
        {
            public string Email { get; set; }
            public string OtpCode { get; set; }
        }


        // Change Password
        [HttpPost("{id}/ChangePassword")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] string newPassword)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound();
                }
                user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
                user.ChangePassword = false;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                await _emailController.SendPasswordChangeNotification(user.Email);

                return Ok("Password changed successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Get All Users
        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            try
            {
                var users = await _context.Users.ToListAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Get User By ID
        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUserById(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            return user;
        }

        // Login method and data get according to id
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest loginRequest)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginRequest.Email);

                // Check if user exists and verify password
                if (user == null)
                {
                    return NotFound("User not found.");
                }

                if (!BCrypt.Net.BCrypt.Verify(loginRequest.Password, user.Password))
                {
                    return BadRequest("Invalid password.");
                }

                // Create claims for the user
                var claims = new[]
                {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
        };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                // Generate JWT token
                var token = new JwtSecurityToken(
                    _configuration["Jwt:Issuer"],
                    _configuration["Jwt:Audience"],
                    claims,
                    expires: DateTime.Now.AddHours(1),
                    signingCredentials: creds
                );

                // Optionally manage sessions (consider JWT stateless approach)
                HttpContext.Session.SetString("UserId", user.Id.ToString());
                HttpContext.Session.SetString("LoggedIn", "true");

                return Ok(new
                {
                    token = new JwtSecurityTokenHandler().WriteToken(token),
                    expiration = token.ValidTo,
                    userId = user.Id,
                    changePassword = user.ChangePassword,
                    userData = new
                    {
                        user.Id,
                        user.FullName,
                        user.Email,
                        user.Role,
                        user.MobileNumber,
                        user.BirthDate,
                        user.CompanyName,
                        user.Continent,
                        user.Country,
                        user.Industry,
                        user.ImagePath

                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Login request class
        public class LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }


        // Add User
        [HttpPost]
        public async Task<IActionResult> UserRegister([FromForm] UserRegisterDto userDto)
        {
            if (userDto == null)
            {
                return BadRequest("User data is null");
            }

            var temporaryPassword = GenerateTemporaryPassword();
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);

            var user = new User
            {
                FullName = userDto.FullName,
                BirthDate = userDto.BirthDate,
                Email = userDto.Email,
                MobileNumber = userDto.MobileNumber,
                CompanyName = userDto.CompanyName,
                Continent = userDto.Continent,
                Country = userDto.Country,
                Industry = userDto.Industry,
                Password = hashedPassword,
                Role = userDto.Role,
                ChangePassword = true,

            };

            if (userDto.UserImage != null)
            {
                var uploadsFolderPath = Path.Combine(_environment.WebRootPath, "upload", "image");
                Directory.CreateDirectory(uploadsFolderPath);
                var filePath = Path.Combine(uploadsFolderPath, userDto.UserImage.FileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await userDto.UserImage.CopyToAsync(fileStream);
                }

                user.ImagePath = Path.Combine("upload", "image", userDto.UserImage.FileName);
            }

            // Check if the email already exists in the database
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == userDto.Email);

            if (existingUser != null)
            {
                // Return a conflict response if the email already exists
                return Conflict("Email already exists");
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // If IsSendViaEmail is true, send login details email

            await _emailController.SendLoginDetails(userDto.Email, temporaryPassword);


            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
        }





        // Get File Path
        [NonAction]
        private string GetFilePath(string productCode)
        {
            return Path.Combine(_environment.WebRootPath, "upload", "image", productCode);
        }

        // Update User
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateDto userUpdateDto)
        {
            if (userUpdateDto == null)
            {
                return BadRequest("User data is null");
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Update only the fields that are allowed to be updated
            user.FullName = userUpdateDto.FullName;
            user.BirthDate = DateOnly.FromDateTime(userUpdateDto.BirthDate);
            user.MobileNumber = userUpdateDto.MobileNumber;
            user.CompanyName = userUpdateDto.CompanyName;
            user.Continent = userUpdateDto.Continent;
            user.Country = userUpdateDto.Country;
            user.Industry = userUpdateDto.Industry;
            user.Role = userUpdateDto.Role;

            // Do not update sensitive fields like Password or Email here

            try
            {
                await _context.SaveChangesAsync();
                return Ok(user);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }

        // Delete User
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound("User not found");
                }

                // Store the user's email before deletion
                string userEmail = user.Email;

                // Delete related ChatMessages
                var relatedMessages = await _context.ChatMessages
                    .Where(m => m.SenderId == id || m.ReceiverId == id)
                    .ToListAsync();
                _context.ChatMessages.RemoveRange(relatedMessages);

                // Delete user's image if exists
                if (!string.IsNullOrEmpty(user.ImagePath))
                {
                    var imagePath = Path.Combine(_environment.WebRootPath, user.ImagePath);
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }

                // Delete user
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                // Send email notification to the user
        await _emailController.SendDeletionNotification(userEmail);
                return Ok("User and related data deleted successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred while deleting the user: {ex.Message}");
            }
        }

        // Method to get the image by user ID
        [HttpGet("{id}/GetUserImage")]
        public async Task<IActionResult> GetUserImage(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null || string.IsNullOrEmpty(user.ImagePath))
            {
                return NotFound("User or image not found");
            }

            var imagePath = Path.Combine(_environment.WebRootPath, user.ImagePath);
            if (!System.IO.File.Exists(imagePath))
            {
                return NotFound("Image file not found");
            }

            var image = System.IO.File.OpenRead(imagePath);
            return File(image, "image/jpeg");
        }

        // Image Update Function
        [HttpPost("UpdateUserImage/{id}")]
        public async Task<IActionResult> UpdateUserImage(int id, [FromForm] UserImageUpdateDto imageDto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound("User not found");
            }

            if (imageDto.UserImage != null)
            {
                var uploadsFolderPath = Path.Combine(_environment.WebRootPath, "upload", "image");
                Directory.CreateDirectory(uploadsFolderPath);

                if (!string.IsNullOrEmpty(user.ImagePath))
                {
                    var oldImagePath = Path.Combine(_environment.WebRootPath, user.ImagePath);
                    if (System.IO.File.Exists(oldImagePath))
                    {
                        System.IO.File.Delete(oldImagePath);
                    }
                }

                var filePath = Path.Combine(uploadsFolderPath, imageDto.UserImage.FileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await imageDto.UserImage.CopyToAsync(fileStream);
                }

                user.ImagePath = Path.Combine("upload", "image", imageDto.UserImage.FileName);
            }

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return Ok(user);
        }


        [HttpGet("filter")]
        public async Task<IActionResult> FilterUsers([FromQuery] string? industry, [FromQuery] string? role)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(industry))
            {
                query = query.Where(u => u.Industry == industry);
            }

            if (!string.IsNullOrEmpty(role))
            {
                query = query.Where(u => u.Role == role);
            }

            var users = await query.ToListAsync();
            return Ok(users);
        }


    }
}
