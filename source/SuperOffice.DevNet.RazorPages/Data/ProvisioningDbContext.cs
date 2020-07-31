﻿using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperOffice.DevNet.Asp.Net.RazorPages.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SuperOffice.DevNet.Asp.Net.RazorPages.Data
{
    public class ProvisioningDbContext : DbContext
    {
        private readonly IHttpRestClient _restClient;

        public ProvisioningDbContext(DbContextOptions<ProvisioningDbContext> options, IHttpRestClient restClient)
            : base(options)
        {
            _restClient = restClient;
        }

        public DbSet<ListEntityModel> Lists { get; set; }
        public DbSet<ListItemModel> UserGroups { get; set; }
        public DbSet<WebPanelEntityModel> WebPanels { get; set; }

        public async Task<IList<ListEntityModel>> GetAllListEntities()
        {
            if (Lists.Count() > 0)
                return await Lists.ToListAsync();

            return await GetAll<ListEntityModel>(Lists, "List");
        }

        public async Task<IList<ListItemModel>> GetAllUserGroups(int listId, int listItemId)
        {
            if (UserGroups.Count() > 0)
                return await UserGroups.ToListAsync();

            return await GetAll<ListItemModel>(UserGroups, $"List/{listId}/Items/{listItemId}/UserGroups");
        }

        /// <summary>
        /// Permanentely delete all web panels owned by your app.
        /// Works in Online only, for registered Apps that send a valid ApplicationToken,
        /// otherwise nothing happens
        /// </summary>
        /// <returns></returns>
        internal async Task DeleteAllAppWebPanels()
        {
            ///api/v1/Agents/List/DeleteAppWebPanels
            var response = await _restClient.Post("/v1/Agents/List/DeleteAppWebPanels", null);

            var webPanels = await GetAllWebPanels();
            var webPanelList = webPanels.Where(p => p.IsAppWebPanel);
            WebPanels.RemoveRange(webPanelList);
            await this.SaveChangesAsync();
        }

        public async Task<bool> UsesGroupsAndHeadings(int listId)
        {
            if (Lists.Count() == 0)
                _ = GetAllListEntities();

            return await Lists.Where(l => l.Id == listId).Select(l => l.UseGroupsAndHeadings).FirstOrDefaultAsync();
        }

        #region Web Panel Implementation

        internal async Task<WebPanelEntityModel> CreateDefaultWebPanel()
        {
            // create a new contact to present in the Create View.
            var result = await _restClient.Get("/v1/List/WebPanel/Items/Default");
            return JsonConvert.DeserializeObject<WebPanelEntityModel>(result);
        }

        internal async Task<IList<ListItemModel>> SetVisibleForUserGroups(int webPanelId, IList<ListItemModel> userGroups)
        {
            var wpEntity = await GetlListEntity("webpanel");
            var obj = JArray.FromObject(userGroups);
            var json = new StringContent(obj.ToString(), System.Text.Encoding.UTF8, "application/json");
            
            ///v1/List/28/Items/2/UserGroups
            var result = await _restClient.Put($"v1/List/{wpEntity.Id}/Items/{webPanelId}/UserGroups", json);
            return JsonConvert.DeserializeObject<IList<ListItemModel>>(result);
        }

        internal async Task DeleteWebPanel(int? id, bool permanent = false)
        {
            var foundWebPanel = await WebPanels.FindAsync(id);

            if (foundWebPanel != null)
            {

                //204 Entity deleted.
                //412 Delete aborted because Entity has changed since the requested If-Unmodified - Since timestamp.

                // must delete the local copy first, otherwise exception is DisposedException is thrown.

                WebPanels.Remove(foundWebPanel);
                await SaveChangesAsync();

                if (!permanent)
                {
                    var deletedContact = await _restClient.Delete($"/v1/List/WebPanel/Items/{id}");
                }
                else
                {
                    var content = new StringContent($"{{ \"id\": {id} }}", System.Text.Encoding.UTF8, "application/json");
                    var deletedContact = await _restClient.Post($"/v1/Agents/List/DeleteWebPanel", content);
                }
            }
        }

        public async Task<IList<WebPanelEntityModel>> GetAllWebPanels()
        {
            if (WebPanels.Count() > 0)
                return await WebPanels.ToListAsync();

            _ = await GetAll<WebPanelEntityModel>(WebPanels, "List/WebPanel/Items");

            _ = await GetAllAppWebPanels();

            return await WebPanels.ToListAsync();
        }

        public async Task<IList<ListItemModel>> GetWebPanelVisibleForUserGroups(int webPanelId)
        {
            ListEntityModel webPanelListEntity = await GetlListEntity("webpanel");

            return await GetAll<ListItemModel>($"List/{webPanelListEntity.Id}/Items/{webPanelId}/UserGroups");
        }

        public async Task<WebPanelEntityModel> SaveAppWebPanel(WebPanelEntityModel webPanel)
        {
            // save web panel to SuperOffice 
            WebPanelEntityModel newWebPanel = await SaveWebPanel(webPanel);

            // add saved contact to local dbcontext
            WebPanels.Add(newWebPanel);
            await SaveChangesAsync();

            return newWebPanel;
        }

        private async Task<WebPanelEntityModel> SaveWebPanel(WebPanelEntityModel webPanel)
        {
            var obj = JObject.FromObject(webPanel);
            var jsonContact = new StringContent(obj.ToString(), System.Text.Encoding.UTF8, "application/json");

            var response = string.Empty;
            if (webPanel.WebPanelId == 0)
            {
                response = await _restClient.Post("/v1/Agents/List/SaveWebPanelEntity", jsonContact);
            }
            else
            {
                response = await _restClient.Put($"/v1/List/WebPanel/Items/{webPanel.WebPanelId}", jsonContact);

            }
            var newWebPanel = JsonConvert.DeserializeObject<WebPanelEntityModel>(response);

            // Mark it as app owned, so this app can edit it straight away...
            newWebPanel.IsAppWebPanel = true;
            return newWebPanel;
        }

        ////Lists don't support PATCH!!!
        //internal bool TryGetChangesForWebPanel(WebPanelEntityModel origWebPanel, WebPanelEntityModel newWebPanel, out JArray patch)
        //{
        //    patch = JArray.Parse("[]");
        //    // pull out only the fields needed to update and add those changes as a PATCH structure
        //    bool updated;
        //    updated = patch.CompareAndReplace(origWebPanel.Name, newWebPanel.Name, "/Name");
        //    updated = patch.CompareAndReplace(origWebPanel.Deleted, newWebPanel.Deleted, "/Deleted");
        //    updated = patch.CompareAndReplace(origWebPanel.OnSalesMarketingPocket, newWebPanel.OnSalesMarketingPocket, "/OnSalesMarketingPocket");
        //    updated = patch.CompareAndReplace(origWebPanel.ProgId, newWebPanel.ProgId, "/ProgId");
        //    updated = patch.CompareAndReplace(origWebPanel.ShowAddressBar, newWebPanel.ShowAddressBar, "/ShowAddressBar");
        //    updated = patch.CompareAndReplace(origWebPanel.ShowMenuBar, newWebPanel.ShowMenuBar, "/ShowMenuBar");
        //    updated = patch.CompareAndReplace(origWebPanel.ShowStatusBar, newWebPanel.ShowStatusBar, "/ShowStatusBar");
        //    updated = patch.CompareAndReplace(origWebPanel.ShowToolBar, newWebPanel.ShowToolBar, "/ShowToolBar");
        //    updated = patch.CompareAndReplace(origWebPanel.Tooltip, newWebPanel.Tooltip, "/Tooltip");
        //    updated = patch.CompareAndReplace(origWebPanel.Url, newWebPanel.Url, "/Url");
        //    updated = patch.CompareAndReplace(origWebPanel.UrlEncoding, newWebPanel.UrlEncoding, "/UrlEncoding");
        //    updated = patch.CompareAndReplace(origWebPanel.VisibleIn, newWebPanel.VisibleIn, "/VisibleIn");
        //    updated = patch.CompareAndReplace(origWebPanel.WindowName, newWebPanel.WindowName, "/WindowName");
        //    return updated;
        //}

        internal async Task UpdateWebPanel(WebPanelEntityModel webPanel)
        {
            try
            {
               webPanel = await SaveWebPanel(webPanel);
               Attach(webPanel).State = EntityState.Modified;
                await SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw;
            }
        }

        private async Task<IList<WebPanelEntityModel>> GetAllAppWebPanels()
        {
            try
            {
                // get all web panel from SuperOffice
                var webPanels = await _restClient.Post("/v1/Agents/List/GetAppWebPanels", new StringContent("")); //$top=611
                var webpanelList = JsonConvert.DeserializeObject<List<WebPanelEntityModel>>(webPanels);

                // make sure to mark them as App specific Webpanels...
                webpanelList.ForEach(w =>
                {
                    var webP = WebPanels.Where(wp => wp.WebPanelId == w.WebPanelId).FirstOrDefault();
                    webP.IsAppWebPanel = true;
                    WebPanels.Update(webP);
                });

                await SaveChangesAsync();

                return webpanelList;
            }
            catch (Exception)
            {
                throw;
            }
        }

        #endregion


        private async Task<ListEntityModel> GetlListEntity(string listName)
        {
            var allLists = await GetAllListEntities();
            var webPanelListEntity = allLists.Where(l => l.ListType.Equals(listName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            return webPanelListEntity;
        }

        private async Task<IList<T>> GetAll<T>(string restUrl) where T : class
        {
            try
            {
                // get all contacts from SuperOffice
                var queryResults = await _restClient.Get($"/v1/{restUrl}");
                //var json = JObject.Parse(webPanels);
                return JsonConvert.DeserializeObject<List<T>>(queryResults);

            }
            catch (Exception ex)
            {
                var e = ex;
                return null;
            }
        }

        private async Task<IList<T>> GetAll<T>(DbSet<T> dbSet, string restUrl) where T : class
        {
            if (dbSet is null)
            {
                throw new ArgumentNullException(nameof(dbSet));
            }

            try
            {
                // get all contacts from SuperOffice
                var queryResults = await _restClient.Get($"/v1/{restUrl}");
                //var json = JObject.Parse(webPanels);
                var listItems = JsonConvert.DeserializeObject<List<T>>(queryResults);

                // save all contacts to local dbcontext
                dbSet.AddRange(listItems);

                await SaveChangesAsync();

                return listItems;
            }
            catch (Exception ex)
            {
                var e = ex;
                return null;
            }
        }
    }
}
