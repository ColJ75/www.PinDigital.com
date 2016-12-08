using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Website.Data;
using Website.Models;

namespace Website.Controllers
{
	public class CMSController : Controller
	{
		private readonly ApplicationDbContext _context;

		public CMSController(ApplicationDbContext context)
		{
			_context = context;
		}

		// GET: CMSPages
		[Authorize("CMS Edit")]
		public IActionResult Index()
		{
			var q = from p in _context.CMSPage
					where p.Status != 2
					select new CMSViewModel(new CMSPage()
					{
						Url = p.Url,
						Title = p.Title,
						Status = p.Status,
						ModifiedBy = p.ModifiedBy,
						ModifiedDate = p.ModifiedDate
					});
			IEnumerable<CMSViewModel> list = q;

			return View(list);
		}

		// GET: CMS/Render/Url
		public async Task<IActionResult> Render(string url)
		{
			if (url == null && HttpContext.Items["CMSUrl"] != null) url = (string)HttpContext.Items["CMSUrl"];
			if (url == null) return NotFound();

			var cmsPage = await _context.CMSPage.SingleOrDefaultAsync(p => p.Url == url);
			if (cmsPage == null) return NotFound();

			// build view model
			CMSViewModel model = new CMSViewModel(cmsPage);
			var version = await _context.CMSPageVersion.SingleOrDefaultAsync(v => v.VersionId == cmsPage.VersionId && v.Status == 1);
			model.Content = version != null ? version.Content : string.Empty;

			// set the view to render, or if the user has the CMS edit claim then set to the editor view
			string view = (HttpContext.User.HasClaim(c => c.Type == "CMS" && c.Value == "Edit")) ? "Editor" : "Render";

			// if the url was passed in via the context, use the render view, otherwise show the detail view
			return View(view, model);
		}

		[Route("CMS/Render/{Url}")]
		[Authorize("CMS Edit")]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Render(string url, [Bind("Url,Content")] CMSPageVersion cmsPageVersion, [Bind("Url,Content")] CMSPageHistory cmsPageHistory)
		{
			if (url == null && HttpContext.Items["CMSUrl"] != null) url = (string)HttpContext.Items["CMSUrl"];
			if (url == null) return NotFound();

			var cmsPage = await _context.CMSPage.SingleOrDefaultAsync(p => p.Url == url);
			if (cmsPage == null) return NotFound();

			// add the new content version to the database
			cmsPageVersion.Status = 1;
			_context.Add(cmsPageVersion);
			await _context.SaveChangesAsync();

			// get current user id
			int userId = int.Parse(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "UserId").Value);

			// add the history item for this update
			cmsPageHistory.VersionId = cmsPageVersion.VersionId;
			cmsPageHistory.Status = 1;
			cmsPageHistory.ModifiedBy = userId;
			cmsPageHistory.ModifiedDate = DateTime.Now;
			_context.Add(cmsPageHistory);
			await _context.SaveChangesAsync();

			// update page info to reflect the update
			cmsPage.VersionId = cmsPageVersion.VersionId;
			cmsPage.ModifiedBy = cmsPageHistory.ModifiedBy;
			cmsPage.ModifiedDate = cmsPageHistory.ModifiedDate;
			_context.Update(cmsPage);
			await _context.SaveChangesAsync();

			// reload the page
			return Redirect("/" + url);
		}

		// GET: CMS/Create
		[Authorize("CMS Edit")]
		public IActionResult Create()
		{
			return View();
		}

		// POST: CMS/Create
		[HttpPost]
		[Authorize("CMS Edit")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Create([Bind("Url,Title")] CMSPage cmsPage)
		{
			if (ModelState.IsValid)
			{
				// get current user id
				int userId = int.Parse(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "UserId").Value);

				// update page properties
				cmsPage.Status = 1;
				cmsPage.ModifiedBy = userId;
				cmsPage.ModifiedDate = DateTime.Now;
				cmsPage.VersionId = 0;
				_context.Add(cmsPage);
				await _context.SaveChangesAsync();

				// update sitemap.json
				Core.Sitemap.Rebuild(_context);

				// redirect back to page list
				return RedirectToAction("Index");
			}
			return View(cmsPage);
		}

		// GET: CMS/Edit/Url
		[Route("CMS/Edit/{Url}")]
		[Authorize("CMS Edit")]
		public async Task<IActionResult> Edit(string url)
		{
			if (url == null) return NotFound();

			var cmsPage = await _context.CMSPage.SingleOrDefaultAsync(m => m.Url == url);
			if (cmsPage == null) return NotFound();

			// build view model incorporating history and versions
			CMSViewModel page = new CMSViewModel(cmsPage);
			var q = from h in _context.CMSPageHistory
					join u in _context.Users on h.ModifiedBy equals u.UserId
					where h.Url == cmsPage.Url
					orderby h.ModifiedDate descending
					select new CMSViewPageHistory()
					{
						ItemId = h.ItemId,
						Url = h.Url,
						Status = h.Status,
						ModifiedDate = h.ModifiedDate,
						ModifiedBy = h.ModifiedBy,
						ModifiedByName = string.Format("{0} {1}", u.FirstName, u.LastName),
						VersionId = h.VersionId
					};
			page.History = await q.ToListAsync();
			page.Versions = await _context.CMSPageVersion.Where(p => p.Url == cmsPage.Url && p.Status == 1).OrderByDescending(v => v.VersionId).ToListAsync();
			page.Content = page.Versions.Count() > 0 ? page.Versions.OrderByDescending(v => v.VersionId).First().Content : string.Empty;

			// if the url was passed in via the context, use the render view, otherwise show the detail view
			return View(page);
		}

		// POST: CMS/Edit/Url
		[Route("CMS/Edit/{Url}")]
		[Authorize("CMS Edit")]
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(string url, [Bind("Url,Title,Status")] CMSPage cmsPage)
		{
			if (url != cmsPage.Url) return NotFound();

			if (ModelState.IsValid)
			{
				try
				{
					cmsPage.ModifiedDate = DateTime.Now;
					_context.Update(cmsPage);
					await _context.SaveChangesAsync();
				}
				catch (DbUpdateConcurrencyException)
				{
					if (!CMSPageExists(cmsPage.Url)) return NotFound();
					else throw;
				}

				// update sitemap.json
				Core.Sitemap.Rebuild(_context);

				return RedirectToAction("Index");
			}
			return View(cmsPage);
		}

		// GET: CMS/UseVersion
		[Route("CMS/UseVersion/{Url}/{Id}")]
		[Authorize("CMS Edit")]
		public async Task<IActionResult> UseVersion(string url, int id)
		{
			CMSPage cmsPage = _context.CMSPage.SingleOrDefault(p => p.Url == url);
			if (cmsPage == null) return NotFound();

			try
			{
				// get current user id
				int userId = int.Parse(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "UserId").Value);

				// update page to reflect version change
				cmsPage.ModifiedBy = userId;
				cmsPage.ModifiedDate = DateTime.Now;
				cmsPage.VersionId = id;
				_context.Update(cmsPage);
				await _context.SaveChangesAsync();

				// add the history item for this update
				CMSPageHistory cmsPageHistory = new CMSPageHistory()
				{
					Url = url,
					VersionId = cmsPage.VersionId,
					Status = 1,
					ModifiedBy = userId,
					ModifiedDate = DateTime.Now
				};
				_context.Add(cmsPageHistory);
				await _context.SaveChangesAsync();
			}
			catch (DbUpdateConcurrencyException)
			{
				if (!CMSPageExists(cmsPage.Url)) return NotFound();
				else throw;
			}
			return RedirectToAction("Edit", new { url = url });
		}

		// GET: CMS/ArchiveVersion
		[Route("CMS/ArchiveVersion/{Url}/{Id}")]
		[Authorize("CMS Edit")]
		public async Task<IActionResult> ArchiveVersion(string url, int id)
		{
			CMSPageVersion cmsPageVersion = _context.CMSPageVersion.SingleOrDefault(p => p.Url == url && p.VersionId == id);
			if (cmsPageVersion == null) return NotFound();

			try
			{
				// update status to reflect change
				cmsPageVersion.Status = 0;
				_context.Update(cmsPageVersion);
				await _context.SaveChangesAsync();
			}
			catch (DbUpdateConcurrencyException)
			{
				if (!CMSPageVersionExists(cmsPageVersion.Url, cmsPageVersion.VersionId)) return NotFound();
				else throw;
			}
			return RedirectToAction("Edit", new { url = url });
		}

		// GET: CMS/Delete/Url
		[Route("CMS/Delete/{Url}")]
		[Authorize("CMS Edit")]
		public async Task<IActionResult> Delete(string url)
		{
			if (url == null)
			{
				return NotFound();
			}

			var cmsPage = await _context.CMSPage
				.SingleOrDefaultAsync(m => m.Url == url);
			if (cmsPage == null)
			{
				return NotFound();
			}

			return View(cmsPage);
		}

		// POST: CMS/Delete/Url
		[Route("CMS/Delete/{Url}")]
		[Authorize("CMS Edit")]
		[HttpPost, ActionName("Delete")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DeleteConfirmed(string url)
		{
			var cmsPage = await _context.CMSPage.SingleOrDefaultAsync(m => m.Url == url);
			cmsPage.Status = 2;
			//_context.CMSPage.Remove(cmsPage);
			await _context.SaveChangesAsync();

			// update sitemap.json
			Core.Sitemap.Rebuild(_context);

			return RedirectToAction("Index");
		}

		private bool CMSPageExists(string url)
		{
			return _context.CMSPage.Any(e => e.Url == url);
		}
		private bool CMSPageVersionExists(string url, int id)
		{
			return _context.CMSPageVersion.Any(e => e.Url == url && e.VersionId == id);
		}
	}
}
