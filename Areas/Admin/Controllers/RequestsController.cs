using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;

namespace SumyCRM.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class RequestsController : Controller
    {
        private readonly DataManager dataManager;
        private readonly UserManager<IdentityUser> _userManager;

        public RequestsController(DataManager dataManager, UserManager<IdentityUser> userManager)
        {
            this.dataManager = dataManager;
            _userManager = userManager;
        }

        // ----------------- helpers -----------------

        private string GetUserId() => _userManager.GetUserId(User) ?? string.Empty;

        private IQueryable<Request> BaseQuery()
        {
            return dataManager.Requests.GetRequests()
                .AsNoTracking()
                .AsQueryable();
        }

        private IQueryable<Request> BaseQueryWithIncludes()
        {
            return dataManager.Requests.GetRequests()
                .AsNoTracking()
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .AsQueryable();
        }

        private IQueryable<Request> ApplyFacilityAccessFilter(IQueryable<Request> query)
        {
            if (User.IsInRole("admin")) return query;

            var userId = GetUserId();

            return query.Where(r =>
                dataManager.UserFacilities.GetUserFacilities()
                    .Any(uf => uf.UserId == userId && uf.FacilityId == r.FacilityId)
            );
        }

        private static IQueryable<Request> ApplyFilters(
            IQueryable<Request> query,
            bool? isCompleted,
            Guid? categoryId,
            Guid? facilityId,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            if (isCompleted.HasValue)
                query = query.Where(r => r.IsCompleted == isCompleted.Value);

            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
                query = query.Where(r => r.CategoryId == categoryId.Value);

            if (dateFrom.HasValue)
                query = query.Where(r => r.DateAdded >= dateFrom.Value);

            if (dateTo.HasValue)
                query = query.Where(r => r.DateAdded <= dateTo.Value);

            if (facilityId.HasValue && facilityId.Value != Guid.Empty)
                query = query.Where(r => r.FacilityId == facilityId.Value);

            return query;
        }

        private static IQueryable<Request> ApplySearch(IQueryable<Request> query, string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return query;

            term = term.Trim();

            if (int.TryParse(term, out var requestNumber))
            {
                return query.Where(r =>
                    r.RequestNumber == requestNumber ||
                    EF.Functions.Like(r.Name ?? "", $"%{term}%") ||
                    EF.Functions.Like(r.Caller ?? "", $"%{term}%") ||
                    EF.Functions.Like(r.Address ?? "", $"%{term}%") ||
                    EF.Functions.Like(r.Text ?? "", $"%{term}%") ||
                    EF.Functions.Like(r.Subcategory ?? "", $"%{term}%") ||
                    (r.Category != null && EF.Functions.Like(r.Category.Title ?? "", $"%{term}%")) ||
                    (r.Facility != null && EF.Functions.Like(r.Facility.Name ?? "", $"%{term}%"))
                );
            }

            return query.Where(r =>
                EF.Functions.Like(r.Name ?? "", $"%{term}%") ||
                EF.Functions.Like(r.Caller ?? "", $"%{term}%") ||
                EF.Functions.Like(r.Address ?? "", $"%{term}%") ||
                EF.Functions.Like(r.Text ?? "", $"%{term}%") ||
                EF.Functions.Like(r.Subcategory ?? "", $"%{term}%") ||
                (r.Category != null && EF.Functions.Like(r.Category.Title ?? "", $"%{term}%")) ||
                (r.Facility != null && EF.Functions.Like(r.Facility.Name ?? "", $"%{term}%"))
            );
        }

        private static bool? ParseStatusToCompleted(string? status)
        {
            return (status ?? "active").Trim().ToLower() switch
            {
                "active" => false,
                "completed" => true,
                "all" => null,
                _ => false
            };
        }

        private static string NormalizeStatus(string? status)
        {
            return (status ?? "active").Trim().ToLower() switch
            {
                "active" => "active",
                "completed" => "completed",
                "all" => "all",
                _ => "active"
            };
        }

        private static DateTime? ParseDateTimeLocal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, out var parsed))
                return parsed;

            return null;
        }

        private async Task FillIndexViewBags(
            Guid? categoryId,
            Guid? facilityId,
            string? dateFrom,
            string? dateTo,
            string status,
            int pageSize)
        {
            ViewBag.Status = NormalizeStatus(status);
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedFacilityId = facilityId;
            ViewBag.DateFrom = dateFrom ?? "";
            ViewBag.DateTo = dateTo ?? "";
            ViewBag.PageSize = pageSize;

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .AsNoTracking()
                .OrderBy(c => c.Title)
                .ToListAsync();

            var facilitiesQuery = dataManager.Facilities.GetFacilities()
                .AsNoTracking()
                .AsQueryable();

            if (!User.IsInRole("admin"))
            {
                var userId = GetUserId();
                facilitiesQuery = facilitiesQuery.Where(f =>
                    dataManager.UserFacilities.GetUserFacilities()
                        .Any(uf => uf.UserId == userId && uf.FacilityId == f.Id)
                );
            }

            ViewBag.Facilities = await facilitiesQuery
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        private async Task FillEditViewBags(bool completed)
        {
            ViewBag.Completed = completed;

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .AsNoTracking()
                .OrderBy(c => c.Title)
                .ToListAsync();

            ViewBag.Facilities = await dataManager.Facilities.GetFacilities()
                .AsNoTracking()
                .OrderBy(f => f.Name)
                .ToListAsync();
        }

        private async Task<int> GetNextRequestNumberAsync()
        {
            var last = await dataManager.Requests.GetRequests()
                .AsNoTracking()
                .MaxAsync(r => (int?)r.RequestNumber);

            return (last ?? 0) + 1;
        }

        static Run R(string text, bool bold = false, string fontSize = "24")
        {
            return new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = "Times New Roman",
                        HighAnsi = "Times New Roman",
                        EastAsia = "Times New Roman",
                        ComplexScript = "Times New Roman"
                    },
                    new FontSize { Val = fontSize },
                    bold ? new Bold() : null
                ),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }
            );
        }

        static void AddTopRightBadgeHeader(WordprocessingDocument doc, MainDocumentPart mainPart, string text)
        {
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            var headerPartId = mainPart.GetIdOfPart(headerPart);

            var tbl = new Table();

            var tblProps = new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new TableJustification { Val = TableRowAlignmentValues.Right },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 12 },
                    new BottomBorder { Val = BorderValues.Single, Size = 12 },
                    new LeftBorder { Val = BorderValues.Single, Size = 12 },
                    new RightBorder { Val = BorderValues.Single, Size = 12 },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None }
                )
            );
            tbl.AppendChild(tblProps);

            var tr = new TableRow();

            var tc = new TableCell();
            tc.AppendChild(new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                new TableCellMargin(
                    new TopMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                    new LeftMargin { Width = "240", Type = TableWidthUnitValues.Dxa },
                    new RightMargin { Width = "240", Type = TableWidthUnitValues.Dxa }
                )
            ));

            var p = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                R(text, bold: true, fontSize: "22")
            );

            tc.Append(p);
            tr.Append(tc);
            tbl.Append(tr);

            headerPart.Header = new Header(tbl);
            headerPart.Header.Save();

            var sectProps = mainPart.Document.Body!.Elements<SectionProperties>().LastOrDefault();
            if (sectProps == null)
            {
                sectProps = new SectionProperties();
                mainPart.Document.Body!.Append(sectProps);
            }

            sectProps.RemoveAllChildren<HeaderReference>();
            sectProps.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerPartId });
        }

        // ----------------- actions -----------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportPrintListDocx(
            string status = "active",
            string? headerLine = null,
            string? term = null,
            string? dateFrom = null,
            string? dateTo = null,
            Guid? categoryId = null,
            Guid? facilityId = null)
        {
            var normalizedStatus = NormalizeStatus(status);
            var completedFilter = ParseStatusToCompleted(normalizedStatus);

            var query = BaseQueryWithIncludes();
            query = ApplyFacilityAccessFilter(query);

            var df = ParseDateTimeLocal(dateFrom);
            var dt = ParseDateTimeLocal(dateTo);

            query = ApplyFilters(query, completedFilter, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var list = await query
                .OrderBy(r => r.IsCompleted)
                .ThenByDescending(r => r.DateAdded)
                .ToListAsync();

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body!;

                AddTopRightBadgeHeader(doc, mainPart, "Інформаційний центр");

                body.Append(
                    PCenteredBold("ІНФОРМАЦІЯ"),
                    PCenteredBold("стосовно скарг мешканців, що надійшли до відділу «Інформаційний центр» на " +
                                  (string.IsNullOrWhiteSpace(headerLine) ? "____________" : headerLine))
                );

                body.Append(new Paragraph(R(" ")));

                var statusText = normalizedStatus switch
                {
                    "completed" => "Виконані",
                    "all" => "Всі",
                    _ => "Активні"
                };

                body.Append(PSimple($"Тип: {statusText}"));
                body.Append(PSimple($"Кількість: {list.Count}"));
                body.Append(PSimple($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}"));

                if (!string.IsNullOrWhiteSpace(term))
                    body.Append(PSimple($"Пошук: {term}"));

                if (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo))
                    body.Append(PSimple($"Період: {dateFrom ?? "—"} — {dateTo ?? "—"}"));

                body.Append(new Paragraph(R(" ")));

                var table = new Table();

                table.AppendChild(new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 6 },
                        new BottomBorder { Val = BorderValues.Single, Size = 6 },
                        new LeftBorder { Val = BorderValues.Single, Size = 6 },
                        new RightBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
                    )
                ));

                table.Append(
                    TR(
                        TH("№"),
                        TH("Номер"),
                        TH("Дата"),
                        TH("Адреса"),
                        TH("ПІБ"),
                        TH("Телефон"),
                        TH("Зміст скарги"),
                        TH("Примітки")
                    )
                );

                int idx = 1;
                foreach (var r in list)
                {
                    table.Append(
                        TR(
                            TD(idx.ToString()),
                            TD(r.RequestNumber.ToString()),
                            TD(r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm")),
                            TD(r.Address ?? ""),
                            TD(r.Name ?? ""),
                            TD(r.Caller ?? ""),
                            TD((r.Text ?? "").Trim()),
                            TD("")
                        )
                    );
                    idx++;
                }

                body.Append(table);
                mainPart.Document.Save();
            }

            var fileName = $"zvernennia_{normalizedStatus}_{DateTime.Now:yyyyMMdd_HHmm}.docx";
            return File(
                ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName
            );

            static Run R(string text, bool bold = false, string fontSize = "24")
            {
                var rp = new RunProperties(
                    new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman", EastAsia = "Times New Roman" },
                    new FontSize { Val = fontSize },
                    new FontSizeComplexScript { Val = fontSize }
                );

                if (bold) rp.Append(new Bold());

                return new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            }

            static Paragraph PCenteredBold(string text) =>
                new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    R(text, bold: true, fontSize: "28")
                );

            static Paragraph PSimple(string text) =>
                new Paragraph(R(text, fontSize: "24"));

            static TableRow TR(params TableCell[] cells)
            {
                var tr = new TableRow();
                foreach (var c in cells) tr.Append(c);
                return tr;
            }

            static TableCell TH(string text) =>
                new TableCell(new Paragraph(R(text, bold: true, fontSize: "24")));

            static TableCell TD(string text) =>
                new TableCell(new Paragraph(R(text, fontSize: "24")));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportPrintAllListDocx(
            string? headerLine,
            string? term,
            string? dateFrom,
            string? dateTo,
            Guid? categoryId = null,
            Guid? facilityId = null)
        {
            return await ExportPrintListDocx(
                status: "all",
                headerLine: headerLine,
                term: term,
                dateFrom: dateFrom,
                dateTo: dateTo,
                categoryId: categoryId,
                facilityId: facilityId
            );
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int page = 1,
            string status = "active",
            Guid? categoryId = null,
            Guid? facilityId = null,
            string? dateFrom = null,
            string? dateTo = null,
            int pageSize = 25)
        {
            if (page < 1) page = 1;

            var allowedPageSizes = new[] { 5, 10, 25, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize))
                pageSize = 25;

            var normalizedStatus = NormalizeStatus(status);

            await FillIndexViewBags(categoryId, facilityId, dateFrom, dateTo, normalizedStatus, pageSize);

            var model = new PaginationViewModel<Request>
            {
                PageItems = new List<Request>(),
                CurrentPage = 1,
                TotalPages = 1,
                PageSize = pageSize
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(
            Guid? id,
            bool completed = false,
            Guid? abonentId = null,
            string? caller = null,
            string? name = null)
        {
            await FillEditViewBags(completed);

            if (!id.HasValue || id.Value == Guid.Empty)
            {
                var nextNumber = await GetNextRequestNumberAsync();

                var model = new Request
                {
                    IsCompleted = false,
                    RequestNumber = nextNumber
                };

                if (abonentId.HasValue && abonentId.Value != Guid.Empty)
                {
                    var abonent = await dataManager.Abonents.GetAbonentByIdAsync(abonentId.Value);
                    if (abonent != null)
                    {
                        model.Caller = abonent.Phone;
                        model.Name = abonent.Name;
                        model.Address = abonent.FullAddress;
                    }
                    else
                    {
                        model.Caller = caller;
                        model.Name = name;
                    }
                }
                else
                {
                    model.Caller = caller;
                    model.Name = name;
                }

                return View(model);
            }

            var entity = await BaseQueryWithIncludes()
                .FirstOrDefaultAsync(r => r.Id == id.Value);

            if (entity == null) return NotFound();

            return View(entity);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Request model, bool completed = false)
        {
            await FillEditViewBags(completed);

            if (!ModelState.IsValid)
                return View(model);

            if (model.Id == Guid.Empty)
            {
                model.IsCompleted = false;
                await dataManager.Requests.SaveRequestAsync(model);
            }
            else
            {
                var entity = await dataManager.Requests.GetRequestByIdAsync(model.Id);
                if (entity == null) return NotFound();

                entity.RequestNumber = model.RequestNumber;
                entity.Name = model.Name;
                entity.Caller = model.Caller;
                entity.Subcategory = model.Subcategory;
                entity.Address = model.Address;
                entity.Text = model.Text;
                entity.CategoryId = model.CategoryId;
                entity.FacilityId = model.FacilityId;
                entity.ForwardedTo = model.ForwardedTo;
                entity.ExecutionProgressInfo = model.ExecutionProgressInfo;
                entity.CustomerInformedOn = model.CustomerInformedOn;
                entity.CustomerFeedback = model.CustomerFeedback;
                entity.CompletedOn = model.CompletedOn;

                await dataManager.Requests.SaveRequestAsync(entity);
            }

            return RedirectToAction(nameof(Index), new { page = 1, status = completed ? "completed" : "active" });
        }

        public async Task<IActionResult> Show(Guid id)
        {
            var model = await BaseQueryWithIncludes()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (model == null) return NotFound();

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CompleteRequest(Guid id, string? returnUrl = null)
        {
            var request = await dataManager.Requests.GetRequestByIdAsync(id);
            if (request == null) return NotFound();

            request.IsCompleted = true;
            request.CompletedOn = DateTime.UtcNow;
            await dataManager.Requests.SaveRequestAsync(request);

            TempData["ToastMessage"] = $"Звернення №{request.RequestNumber} позначено як виконане.";
            TempData["ToastType"] = "success";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index), new { status = "active" });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(Guid id, string? returnUrl = null)
        {
            if (!User.IsInRole("admin"))
                return Forbid();

            var entity = await dataManager.Requests.GetRequestByIdAsync(id);
            if (entity == null) return NotFound();

            if (entity.IsCompleted)
            {
                await dataManager.Requests.DeleteRequestAsync(entity);

                TempData["ToastMessage"] = $"Звернення №{entity.RequestNumber} видалено.";
                TempData["ToastType"] = "danger";
            }
            else
            {
                TempData["ToastMessage"] = "Видаляти можна лише виконані звернення.";
                TempData["ToastType"] = "warning";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index), new { page = 1, status = "completed" });
        }

        public IActionResult LoadRequests()
        {
            var query = BaseQuery().Where(r => !r.IsCompleted);
            query = ApplyFacilityAccessFilter(query);

            return Json(new
            {
                success = true,
                totalQuantity = query.Count()
            });
        }

        [HttpGet]
        public async Task<IActionResult> Search(string term, string status = "active")
        {
            var completedFilter = ParseStatusToCompleted(status);

            var query = BaseQueryWithIncludes();
            query = ApplyFacilityAccessFilter(query);
            query = ApplyFilters(query, completedFilter, null, null, null, null);
            query = ApplySearch(query, term);

            var list = await query
                .OrderBy(r => r.IsCompleted)
                .ThenByDescending(r => r.DateAdded)
                .Take(300)
                .Select((r) => new
                {
                    id = r.Id,
                    requestNumber = r.RequestNumber,
                    name = r.Name,
                    caller = r.Caller,
                    facility = r.Facility != null ? r.Facility.Name : "",
                    category = r.Category != null ? r.Category.Title : "",
                    subcategory = r.Subcategory,
                    address = r.Address,
                    text = r.Text,
                    nameAudio = r.NameAudioFilePath,
                    addressAudio = r.AddressAudioFilePath,
                    audio = r.AudioFilePath,
                    date = r.DateAdded,
                    isCompleted = r.IsCompleted
                })
                .ToListAsync();

            var result = list.Select((r, idx) => new
            {
                index = idx + 1,
                r.id,
                r.requestNumber,
                r.name,
                r.caller,
                r.facility,
                r.category,
                r.subcategory,
                r.address,
                r.text,
                r.nameAudio,
                r.addressAudio,
                r.audio,
                date = r.date.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                r.isCompleted
            });

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            string? term,
            string status = "active",
            Guid? categoryId = null,
            Guid? facilityId = null,
            string? dateFrom = null,
            string? dateTo = null,
            int page = 1,
            int pageSize = 25)
        {
            if (page < 1) page = 1;

            var allowedPageSizes = new[] { 5, 10, 25, 50, 100 };
            if (!allowedPageSizes.Contains(pageSize))
                pageSize = 25;

            var normalizedStatus = NormalizeStatus(status);
            var completedFilter = ParseStatusToCompleted(normalizedStatus);

            var query = BaseQueryWithIncludes();
            query = ApplyFacilityAccessFilter(query);

            var df = ParseDateTimeLocal(dateFrom);
            var dt = ParseDateTimeLocal(dateTo);

            query = ApplyFilters(query, completedFilter, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var total = await query.CountAsync();

            var totalPages = total > 0
                ? (int)Math.Ceiling(total / (double)pageSize)
                : 1;

            if (page > totalPages)
                page = totalPages;

            var rawItems = await query
                .OrderBy(r => r.IsCompleted)
                .ThenByDescending(r => r.DateAdded)
                .Select(r => new
                {
                    r.Id,
                    r.RequestNumber,
                    r.Name,
                    r.Caller,
                    Facility = r.Facility != null ? r.Facility.Name : "",
                    Category = r.Category != null ? r.Category.Title : "",
                    r.Subcategory,
                    r.Address,
                    r.Text,
                    Audio = r.AudioFilePath,
                    NameAudio = r.NameAudioFilePath,
                    AddressAudio = r.AddressAudioFilePath,
                    r.DateAdded,
                    r.IsCompleted
                })
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = rawItems.Select((r, idx) => new
            {
                index = (page - 1) * pageSize + idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility,
                category = r.Category,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                audio = r.Audio,
                nameAudio = r.NameAudio,
                addressAudio = r.AddressAudio,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(new
            {
                items,
                total,
                currentPage = page,
                pageSize,
                totalPages
            });
        }

        [HttpGet]
        public async Task<IActionResult> Print(Guid id)
        {
            var model = await BaseQueryWithIncludes()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (model == null) return NotFound();

            var q = ApplyFacilityAccessFilter(BaseQueryWithIncludes().Where(x => x.Id == id));
            var allowed = await q.AnyAsync();
            if (!allowed) return Forbid();

            var facilitiesQuery = dataManager.Facilities.GetFacilities()
                .AsNoTracking()
                .AsQueryable();

            if (!User.IsInRole("admin"))
            {
                var userId = GetUserId();
                facilitiesQuery = facilitiesQuery.Where(f =>
                    dataManager.UserFacilities.GetUserFacilities()
                        .Any(uf => uf.UserId == userId && uf.FacilityId == f.Id)
                );
            }

            ViewBag.Facilities = await facilitiesQuery
                .OrderBy(f => f.Name)
                .ToListAsync();

            return View("Print", model);
        }

        [HttpGet]
        public async Task<IActionResult> PrintList(
            string? term,
            string status = "active",
            Guid? categoryId = null,
            Guid? facilityId = null,
            string? dateFrom = null,
            string? dateTo = null)
        {
            var normalizedStatus = NormalizeStatus(status);
            var completedFilter = ParseStatusToCompleted(normalizedStatus);

            var query = BaseQueryWithIncludes();
            query = ApplyFacilityAccessFilter(query);

            var df = ParseDateTimeLocal(dateFrom);
            var dt = ParseDateTimeLocal(dateTo);

            query = ApplyFilters(query, completedFilter, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var list = await query
                .OrderBy(r => r.IsCompleted)
                .ThenByDescending(r => r.DateAdded)
                .ToListAsync();

            ViewBag.Status = normalizedStatus;
            ViewBag.Term = term ?? "";
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedFacilityId = facilityId;
            ViewBag.DateFrom = dateFrom;
            ViewBag.DateTo = dateTo;

            return View("PrintList", list);
        }

        [HttpGet]
        public async Task<IActionResult> PrintAllList(
            string? term,
            Guid? categoryId,
            Guid? facilityId,
            string? dateFrom,
            string? dateTo)
        {
            return await PrintList(
                term: term,
                status: "all",
                categoryId: categoryId,
                facilityId: facilityId,
                dateFrom: dateFrom,
                dateTo: dateTo
            );
        }

        public class PrintFieldsDto
        {
            public int RequestNumber { get; set; }
            public string? Name { get; set; }
            public string? Address { get; set; }
            public string? Caller { get; set; }
            public string? Text { get; set; }
            public Guid FacilityId { get; set; }
            public string? ForwardedTo { get; set; }
            public string? ExecutionProgressInfo { get; set; }
            public string? CustomerFeedback { get; set; }
            public DateTime? CustomerInformedOn { get; set; }
            public DateTime? CompletedOn { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePrintFields(Guid id, PrintFieldsDto dto)
        {
            var allowed = await ApplyFacilityAccessFilter(
                BaseQueryWithIncludes().Where(x => x.Id == id)
            ).AnyAsync();

            if (!allowed) return Forbid();

            var entity = await dataManager.Requests.GetRequestByIdAsync(id);
            if (entity == null) return NotFound();

            entity.RequestNumber = dto.RequestNumber;
            entity.Name = dto.Name?.Trim();
            entity.Address = dto.Address?.Trim();
            entity.Caller = dto.Caller?.Trim();
            entity.Text = dto.Text?.Trim();
            entity.FacilityId = dto.FacilityId;
            entity.ForwardedTo = dto.ForwardedTo?.Trim();
            entity.ExecutionProgressInfo = dto.ExecutionProgressInfo?.Trim();
            entity.CustomerFeedback = dto.CustomerFeedback?.Trim();
            entity.CustomerInformedOn = dto.CustomerInformedOn;
            entity.CompletedOn = dto.CompletedOn;

            await dataManager.Requests.SaveRequestAsync(entity);

            TempData["ToastMessage"] = $"Дані друк-форми для звернення №{entity.RequestNumber} збережено.";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Print), new { id });
        }
    }
}