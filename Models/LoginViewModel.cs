using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace TraineeApplication.Model
{
	public class LoginViewModel
	{
		[Required(ErrorMessage = "Введіть логін")]
		[Display(Name = ("Логін"))]
		public string Username { get; set; }

		[Required(ErrorMessage = "Введіть пароль")]
		[UIHint("password")]
		[Display(Name = "Пароль")]
		public string Password { get; set; }

		[Display(Name = "Запам'ятати мене")]
		public bool RememberMe { get; set; }
	}
}
