using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SumyCRM.Areas.Admin.Models;
using SumyCRM.Data;
using SumyCRM.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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
                .Include(r => r.Category)
                .Include(r => r.Facility)
                .AsQueryable();
        }

        private IQueryable<Request> ApplyFacilityAccessFilter(IQueryable<Request> query)
        {
            if (User.IsInRole("admin")) return query;

            var userId = GetUserId();

            // EXISTS (UserFacilities)
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
                query = query.Where(r => r.DateAdded >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(r => r.DateAdded < dateTo.Value.Date.AddDays(1)); // inclusive day

            if (facilityId.HasValue && facilityId.Value != Guid.Empty)
                query = query.Where(r => r.FacilityId == facilityId.Value);

            return query;
        }

        private static IQueryable<Request> ApplySearch(IQueryable<Request> query, string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return query;

            term = term.Trim().ToLower();

            return query.Where(r =>
                r.RequestNumber.ToString().ToLower().Contains(term) ||
                (r.Name ?? "").ToLower().Contains(term) ||
                (r.Caller ?? "").ToLower().Contains(term) ||
                (r.Facility != null ? r.Facility.Name : "").ToLower().Contains(term) ||
                (r.Subcategory ?? "").ToLower().Contains(term) ||
                (r.Address ?? "").ToLower().Contains(term) ||
                (r.Text ?? "").ToLower().Contains(term) ||
                (r.Category != null ? r.Category.Title : "").ToLower().Contains(term)
            );
        }

        private async Task FillIndexViewBags(Guid? categoryId, Guid? facilityId, DateTime? dateFrom, DateTime? dateTo, bool completed)
        {
            ViewBag.AreCompleted = completed;
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedFacilityId = facilityId; // ✅
            ViewBag.DateFrom = dateFrom?.ToString("yyyy-MM-dd");
            ViewBag.DateTo = dateTo?.ToString("yyyy-MM-dd");

            ViewBag.Categories = await dataManager.Categories.GetCategories()
                .OrderBy(c => c.Title)
                .ToListAsync();

            // ✅ Facilities list (with access filter for non-admin)
            var facilitiesQuery = dataManager.Facilities.GetFacilities().AsQueryable();

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
                .OrderBy(c => c.Title)
                .ToListAsync();

            ViewBag.Facilities = await dataManager.Facilities.GetFacilities()
                .OrderBy(f => f.Name)
                .ToListAsync();
        }
        private async Task<int> GetNextRequestNumberAsync()
        {
            // Якщо таблиця порожня — буде 1
            var last = await dataManager.Requests.GetRequests()
                .MaxAsync(r => (int?)r.RequestNumber);

            return (last ?? 0) + 1;
        }
        static Run R(string text, bool bold = false, string fontSize = "24") // 12pt = 24
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

            // Header content: right-aligned "badge" made of a 1-cell table with borders/padding
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
                // padding (like your HTML badge)
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

            // Attach header to section properties (first section)
            var sectProps = mainPart.Document.Body!.Elements<SectionProperties>().LastOrDefault();
            if (sectProps == null)
            {
                sectProps = new SectionProperties();
                mainPart.Document.Body!.Append(sectProps);
            }

            // NOTE: This applies to all pages. If you need FIRST PAGE ONLY, see note below.
            sectProps.RemoveAllChildren<HeaderReference>();
            sectProps.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerPartId });

            // If you want a different first page header, Word needs "DifferentFirstPageHeaderFooter"
            // and a HeaderReference(Type=First). See note below.
        }
        // ----------------- actions -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportPrintListDocx(
            bool isCompleted,
            string? headerLine,
            string? term,
            string? dateFrom,
            string? dateTo,
            Guid? categoryId = null,
            Guid? facilityId = null)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            // ✅ ONLY one status
            query = ApplyFilters(query, isCompleted: isCompleted, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            // stable order (same as Index)
            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body!;

                AddTopRightBadgeHeader(doc, mainPart, "Інформаційний центр");

                // Title
                body.Append(
                    PCenteredBold("ІНФОРМАЦІЯ"),
                    PCenteredBold("стосовно скарг мешканців, що надійшли до відділу «Інформаційний центр» на " +
                                  (string.IsNullOrWhiteSpace(headerLine) ? "____________" : headerLine))
                );

                body.Append(new Paragraph(R(" ")));

                // Meta
                body.Append(PSimple($"Тип: {(isCompleted ? "Виконані" : "Активні")}"));
                body.Append(PSimple($"Кількість: {list.Count}"));
                body.Append(PSimple($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}"));

                if (!string.IsNullOrWhiteSpace(term))
                    body.Append(PSimple($"Пошук: {term}"));

                if (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo))
                    body.Append(PSimple($"Період: {dateFrom ?? "—"} — {dateTo ?? "—"}"));

                body.Append(new Paragraph(R(" ")));

                // Table
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

            var fileName = $"zvernennia_{(isCompleted ? "done" : "active")}_{DateTime.Now:yyyyMMdd_HHmm}.docx";
            return File(
                ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName
            );

            // ---- helpers (Times New Roman) ----
            static Run R(string text, bool bold = false, string fontSize = "24")
            {
                // fontSize: "24" = 12pt, "28" = 14pt, etc. (half-points)
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
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            // BOTH statuses
            query = ApplyFilters(query, isCompleted: null, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            // Active first, then completed (stable)
            var list = await query
                .OrderBy(r => r.IsCompleted)              // false (active) first
                .ThenByDescending(r => r.DateAdded)
                .ToListAsync();
            
            
            // Build docx
            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());
                var body = mainPart.Document.Body!;

                AddTopRightBadgeHeader(doc, mainPart, "Інформаційний центр");

                // Title
                body.Append(
                    PCenteredBold("ІНФОРМАЦІЯ"),
                    PCenteredBold("стосовно скарг мешканців, що надійшли до відділу «Інформаційний центр» на " + (string.IsNullOrWhiteSpace(headerLine) ? "____________" : headerLine))
                );

                body.Append(new Paragraph(new Run(new Text(" "))));

                // Meta
                body.Append(PSimple($"Кількість: {list.Count}"));
                body.Append(PSimple($"Дата: {DateTime.Now:dd.MM.yyyy HH:mm}"));
                if (!string.IsNullOrWhiteSpace(dateFrom) || !string.IsNullOrWhiteSpace(dateTo))

                body.Append(new Paragraph(new Run(new Text(" "))));

                // Table
                var table = new Table();

                // basic borders
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

                // header row
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
                            TD((r.RequestNumber).ToString()),
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

            var fileName = $"zvernennia_{DateTime.Now:yyyyMMdd_HHmm}.docx";
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                fileName);

            // ---- local helpers ----
            static Paragraph PCenteredBold(string text) =>
                new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    R(text, bold: true, fontSize: "28") // 14pt for title
                );

            static Paragraph PSimple(string text) =>
                    new Paragraph(R(text, fontSize: "24")); // 12pt body text

            static TableRow TR(params TableCell[] cells)
            {
                var tr = new TableRow();
                foreach (var c in cells) tr.Append(c);
                return tr;
            }

            static TableCell TH(string text) =>
                new TableCell(
                    new Paragraph(R(text, bold: true, fontSize: "24"))
                );

            static TableCell TD(string text) =>
                new TableCell(
                    new Paragraph(R(text, fontSize: "24"))
                );
        }

        public async Task<IActionResult> Index(
            int page = 1,
            bool completed = false,
            Guid? categoryId = null,
            Guid? facilityId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            const int pageSize = 5;

            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);
            query = ApplyFilters(query, completed, categoryId, facilityId, dateFrom, dateTo);

            var total = await query.CountAsync();

            var pageItems = await query
                .OrderByDescending(r => r.RequestNumber)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            await FillIndexViewBags(categoryId, facilityId, dateFrom, dateTo, completed);

            var model = new PaginationViewModel<Request>
            {
                PageItems = pageItems,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                PageSize = pageSize
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid? id, bool completed = false)
        {
            await FillEditViewBags(completed);

            if (!id.HasValue || id.Value == Guid.Empty)
            {
                var nextNumber = await GetNextRequestNumberAsync();
                return View(new Request
                {
                    IsCompleted = false,
                    RequestNumber = nextNumber

                });
            }

            var entity = await BaseQuery()
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

                await dataManager.Requests.SaveRequestAsync(entity);
            }

            return RedirectToAction(nameof(Index), new { page = 1, completed });
        }

        public async Task<IActionResult> Show(Guid id)
        {
            var model = await BaseQuery()
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
            await dataManager.Requests.SaveRequestAsync(request);

            TempData["ToastMessage"] = $"Звернення №{request.RequestNumber} позначено як виконане.";
            TempData["ToastType"] = "success";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index), new { completed = false }); // fallback
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

            return RedirectToAction(nameof(Index), new { page = 1, completed = true }); // fallback
        }

        // used by your "counter" widget
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
        public async Task<IActionResult> Search(string term, bool completed = false)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);
            query = ApplyFilters(query, completed, null, null, null, null);
            query = ApplySearch(query, term);

            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .Take(300)
                .ToListAsync();

            var result = list.Select((r, idx) => new
            {
                index = idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility != null ? r.Facility.Name : "",
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                nameAudio = r.NameAudioFilePath,
                addressAudio = r.AddressAudioFilePath,
                audio = r.AudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> All(
            Guid? categoryId = null,
            Guid? facilityId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            // Completed toggle is not needed here, but we keep ViewBags consistent
            await FillIndexViewBags(categoryId, facilityId, dateFrom, dateTo, completed: false);

            // we don’t need initial items because page loads via AJAX
            var model = new PaginationViewModel<Request>
            {
                PageItems = new List<Request>(),
                CurrentPage = 1,
                TotalPages = 1,
                PageSize = 999999
            };

            return View("All", model); // Views/Admin/Requests/All.cshtml
        }

        [HttpGet]
        public async Task<IActionResult> GetAllList(
             string? term,
             Guid? categoryId,
             Guid? facilityId,
             string? dateFrom,
             string? dateTo,
             int page = 1,
             int pageSize = 5)
        {
            if (page < 1) page = 1;
            if (pageSize < 5) pageSize = 5;

            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            // BOTH statuses
            query = ApplyFilters(query, isCompleted: null, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var total = await query.CountAsync();

            var list = await query
                .OrderBy(r => r.IsCompleted)                 // false (active) first, true (completed) last
                .ThenByDescending(r => r.DateAdded)          // newest first inside each group
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = list.Select((r, idx) => new
            {
                index = (page - 1) * pageSize + idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility != null ? r.Facility.Name : "",
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                audio = r.AudioFilePath,
                nameAudio = r.NameAudioFilePath,
                addressAudio = r.AddressAudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(new
            {
                items,
                total,
                currentPage = page,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }


        [HttpGet]
        public async Task<IActionResult> GetList(
            string? term,
            bool? isCompleted,
            Guid? categoryId,
            Guid? facilityId,
            string? dateFrom,
            string? dateTo,
            int page = 1,
            int pageSize = 5)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            query = ApplyFilters(query, isCompleted, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var total = await query.CountAsync();

            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = list.Select((r, idx) => new
            {
                index = (page - 1) * pageSize + idx + 1,
                id = r.Id,
                requestNumber = r.RequestNumber,
                name = r.Name,
                caller = r.Caller,
                facility = r.Facility != null ? r.Facility.Name : "",
                category = r.Category?.Title,
                subcategory = r.Subcategory,
                address = r.Address,
                text = r.Text,
                audio = r.AudioFilePath,
                nameAudio = r.NameAudioFilePath,
                addressAudio = r.AddressAudioFilePath,
                date = r.DateAdded.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                isCompleted = r.IsCompleted
            });

            return Json(new
            {
                items,
                total,
                currentPage = page,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }
        [HttpGet]
        public async Task<IActionResult> Print(Guid id)
        {
            var model = await BaseQuery()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (model == null) return NotFound();

            // (необов’язково) доступ по facility як в інших місцях
            var q = ApplyFacilityAccessFilter(BaseQuery().Where(x => x.Id == id));
            var allowed = await q.AnyAsync();
            if (!allowed) return Forbid();

            return View("Print", model); // Views/Admin/Requests/Print.cshtml
        }
        [HttpGet]
        public async Task<IActionResult> PrintList(
            string? term,
            bool? isCompleted,
            Guid? categoryId,
            Guid? facilityId,
            string? dateFrom,
            string? dateTo)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            query = ApplyFilters(query, isCompleted, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            // IMPORTANT: order for printing
            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();

            // pass “header info” to the print view
            ViewBag.AreCompleted = isCompleted ?? false;
            ViewBag.Term = term ?? "";
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedFacilityId = facilityId;
            ViewBag.DateFrom = dateFrom;
            ViewBag.DateTo = dateTo;

            return View("PrintList", list); // create Views/Admin/Requests/PrintList.cshtml
        }
        [HttpGet]
        public async Task<IActionResult> PrintAllList(
            string? term,
            Guid? categoryId,
            Guid? facilityId,
            string? dateFrom,
            string? dateTo)
        {
            var query = BaseQuery();
            query = ApplyFacilityAccessFilter(query);

            DateTime? df = null, dt = null;
            if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var tmpDf)) df = tmpDf;
            if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var tmpDt)) dt = tmpDt;

            // BOTH statuses => isCompleted = null
            query = ApplyFilters(query, isCompleted: null, categoryId, facilityId, df, dt);
            query = ApplySearch(query, term);

            var list = await query
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();

            ViewBag.Term = term ?? "";
            ViewBag.DateFrom = dateFrom ?? "";
            ViewBag.DateTo = dateTo ?? "";
            ViewBag.SelectedCategoryId = categoryId;     
            ViewBag.SelectedFacilityId = facilityId;     

            return View("PrintAllList", list); // create view
        }
    }
}
