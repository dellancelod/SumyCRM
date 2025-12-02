using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;
using SumyCRM.Services;
using TraineeApplication.Model;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ScheduleController : Controller
    {
        private readonly DataManager dataManager;
        private readonly IWebHostEnvironment hostEnvironment;
        private readonly IScheduleAudioService scheduleAudioService;
        private readonly string _soundsPath;
        public ScheduleController(DataManager dataManager, IWebHostEnvironment hostEnvironment, 
            IScheduleAudioService scheduleAudioService, IConfiguration config)
        {
            this.dataManager = dataManager;
            this.hostEnvironment = hostEnvironment;
            this.scheduleAudioService = scheduleAudioService;
            _soundsPath = config["ScheduleAudio:AsteriskSoundsPath"]
                          ?? "/usr/share/asterisk/sounds/en/";
        }
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 7; // Items per page

            var schedules = await dataManager.Schedules.GetSchedules()
                .OrderBy(x => x.Hidden)
                .ToListAsync();

            var pageItems = schedules
                .Skip((page - 1) * pageSize)
                .Take(pageSize);


            var model = new PaginationViewModel<Schedule>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(schedules.Count() / (double)pageSize)
            };

            return View(model);
        }
        public async Task<IActionResult> Edit(Guid id)
        {
            var entity = id == default ? new Schedule() : await dataManager.Schedules.GetScheduleByIdAsync(id);

            return View(entity);
        }
        [HttpPost]
        [RequestSizeLimit(512 * 1024 * 1024)]
        public async Task<IActionResult> Edit(Schedule model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return View(model);

            if (!dataManager.Schedules.IsUniqueScheduleNumber(model.Number, model.Id))
            {
                ModelState.AddModelError("Number", "Такий номер вже внесений");
                return View(model);
            }

            // 1) Генерируем аудио из текста Time (и Number)
            try
            {
                var audioFileName = await scheduleAudioService.GenerateAudioAsync(model, cancellationToken);

                // 2) Сохраняем имя файла (без расширения) в модель
                model.AudioFileName = audioFileName;
            }
            catch (Exception ex)
            {
                // Если TTS упал — можно добавить ошибку и не сохранять
                ModelState.AddModelError(string.Empty, "Помилка генерації аудіо: " + ex.Message);
                return View(model);
            }

            // 3) Сохраняем расписание в БД
            await dataManager.Schedules.SaveScheduleAsync(model);

            return RedirectToAction(nameof(ScheduleController.Index),
                nameof(ScheduleController).Replace("Controller", string.Empty));
        }
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            var schedule = await dataManager.Schedules.GetScheduleByIdAsync(id);
            if (schedule != null && !string.IsNullOrWhiteSpace(schedule.AudioFileName))
            {
                // AudioFileName у тебя без расширения: "schedule_12_uk"
                var fileName = schedule.AudioFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    ? schedule.AudioFileName
                    : schedule.AudioFileName + ".wav";

                var fullPath = Path.Combine(_soundsPath, fileName);

                try
                {
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
                catch (Exception ex)
                {
                }
            }

            await dataManager.Schedules.DeleteScheduleAsync(id);

            // тут, думаю, у тебя вообще должна быть редирект на ScheduleController, а не CategoriesController
            return RedirectToAction(
                nameof(ScheduleController.Index),
                nameof(ScheduleController).Replace("Controller", string.Empty)
            );
        }
    }
}
