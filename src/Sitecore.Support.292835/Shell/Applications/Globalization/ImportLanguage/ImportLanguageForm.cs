// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ImportLanguage.form.cs" company="Sitecore">
//   Copyright (c) Sitecore. All rights reserved.
// </copyright>
// <summary>
//   ImportLanguage.form class
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using Sitecore.Exceptions;

namespace Sitecore.Support.Shell.Applications.Globalization.ImportLanguage
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text.RegularExpressions;
  using System.Web;
  using System.Web.UI;
  using System.Xml;

  using Configuration;
  using Diagnostics;
  using IO;
  using Jobs;
  using Sitecore.Collections;
  using Sitecore.Data;
  using Sitecore.Data.Events;
  using Sitecore.Data.Fields;
  using Sitecore.Data.Items;
  using Sitecore.Globalization;
  using Sitecore.Utils;
  using Sitecore.Web.UI.HtmlControls;
  using Sitecore.Web.UI.Pages;
  using Sitecore.Web.UI.Sheer;
  using Xml;

  /// <summary>
  /// Represents a ImportLanguageForm.
  /// </summary>
  public class ImportLanguageForm : WizardForm
  {
    #region Controls

    /// <summary>
    /// The error text.
    /// </summary>
    protected Memo ErrorText;

    /// <summary>
    /// The databases.
    /// </summary>
    protected Listbox Databases;

    /// <summary>
    /// The language file.
    /// </summary>
    protected Edit LanguageFile;

    /// <summary>
    /// The result text.
    /// </summary>
    protected Memo ResultText;

    /// <summary>
    /// The language list.
    /// </summary>
    protected Scrollbox LanguageList;

    /// <summary>
    /// The language file info.
    /// </summary>
    protected Literal LanguageFileInfo;

    /// <summary>
    /// The selected languages.
    /// </summary>
    protected Border SelectedLanguages;

    #endregion

    #region Protected methods

    /// <summary>
    /// Called when the active page has been changed.
    /// </summary>
    /// <param name="page">
    /// The page that has been entered.
    /// </param>
    /// <param name="oldPage">
    /// The page that was left.
    /// </param>
    /// <contract>
    ///   <requires name="page" condition="not null"/>
    ///   <requires name="oldPage" condition="not null"/>
    /// </contract>
    protected override void ActivePageChanged(string page, string oldPage)
    {
      Assert.ArgumentNotNull(page, "page");
      Assert.ArgumentNotNull(oldPage, "oldPage");

      base.ActivePageChanged(page, oldPage);

      if (page == "Ready")
      {
        this.NextButton.Header = Texts.IMPORT;
      }

      if (page == "Importing")
      {
        this.NextButton.Disabled = true;
        this.BackButton.Disabled = true;
        this.CancelButton.Disabled = true;

        SheerResponse.Timer("StartImport", 10);
      }
    }

    /// <summary>
    /// Called when the active page is changing.
    /// </summary>
    /// <param name="page">
    /// The page that is being left.
    /// </param>
    /// <param name="newpage">
    /// The new page that is being entered.
    /// </param>
    /// <returns>
    /// True, if the change is allowed, otherwise false.
    /// </returns>
    /// <remarks>
    /// Set the <c>newpage</c> parameter to another page ID to control the
    /// path through the wizard pages.
    /// </remarks>
    /// <contract>
    ///   <requires name="page" condition="not null"/>
    ///   <requires name="newpage" condition="not null"/>
    /// </contract>
    protected override bool ActivePageChanging(string page, ref string newpage)
    {
      Assert.ArgumentNotNull(page, "page");
      Assert.ArgumentNotNull(newpage, "newpage");

      if (page == "Retry" && newpage == "Importing")
      {
        newpage = "File";
        this.NextButton.Disabled = false;
        this.BackButton.Disabled = false;
        this.CancelButton.Disabled = false;
      }

      if (page == "File" && newpage == "Languages")
      {
        string filename = this.LanguageFile.Value;

        if (filename.Length == 0)
        {
          SheerResponse.Alert(Texts.PLEASE_SPECIFY_A_LANGUAGE_FILE);
          return false;
        }

        if (filename.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || filename == ".")
        {
          SheerResponse.Alert(Texts.THE_NAME_CONTAINS_INVALID_CHARACTERS);
          return false;
        }

        if (!FileUtil.Exists(filename))
        {
          SheerResponse.Alert(Translate.Text(Texts.THE_LANGUAGE_FILE_0_DOES_NOT_EXIST, filename));
          return false;
        }

        if (!this.RenderLanguages(filename))
        {
          SheerResponse.Alert(Translate.Text(Texts.THE_FORMAT_OF_THE_LANGUAGE_FILE_0_IS_INVALID, filename));
          return false;
        }
      }

      if (page == "Languages" && newpage == "Ready")
      {
        this.RenderInfo();
      }

      return base.ActivePageChanging(page, ref newpage);
    }

    /// <summary>
    /// Browses the specified <c>args</c>.
    /// </summary>
    /// <param name="args">
    /// The arguments.
    /// </param>
    [HandleMessage("import:browse", true)]
    protected void Browse([NotNull] ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");

      if (args.IsPostBack)
      {
        if (args.Result.Length > 0 && args.Result != "undefined")
        {
          this.LanguageFile.Value = args.Result;
        }
      }
      else
      {
        string languageFile = Path.GetFileName(this.LanguageFile.Value);
        Assert.IsNotNull(languageFile, "language file");
        Sitecore.Shell.Framework.Files.OpenFile(
          Texts.OPEN_LANGUAGE_FILE,
          Texts.SELECT_THE_LANGUAGE_FILE_THAT_YOU_WISH_TO_OPEN,
          "Network/16x16/earth_location.png",
          Texts.OPEN,
          "/",
          FileUtil.UnmapPath(Settings.MediaFolder),
          languageFile,
          string.Empty);

        args.WaitForPostBack();
      }
    }

    /// <summary>
    /// Checks the status.
    /// </summary>
    protected void CheckStatus()
    {
      var handleId = Context.ClientPage.ServerProperties["handle"] as string;
      Assert.IsNotNullOrEmpty(handleId, "handle not found");

      Handle handle = Handle.Parse(handleId);

      Job job = JobManager.GetJob(handle);

      if (job.Status.Failed)
      {
        this.Active = "Retry";
        this.NextButton.Disabled = true;
        this.BackButton.Disabled = false;
        this.CancelButton.Disabled = false;
        this.ErrorText.Value = StringUtil.StringCollectionToString(job.Status.Messages);
        return;
      }

      string text;

      if (job.Status.State == JobState.Running)
      {
        text = Translate.Text(Texts.PROCESSED_0_OF_1, job.Status.Processed, job.Status.Total);
      }
      else
      {
        text = Texts.QUEUED;
      }

      if (job.IsDone)
      {
        this.Active = "LastPage";
        this.BackButton.Disabled = true;
        this.ResultText.Value = StringUtil.StringCollectionToString(job.Status.Messages);
        return;
      }

      SheerResponse.SetInnerHtml("Status", text);
      SheerResponse.Timer("CheckStatus", Settings.Publishing.PublishDialogPollingInterval);
    }

    /// <summary>
    /// Raises the load event.
    /// </summary>
    /// <param name="e">
    /// The <see cref="System.EventArgs"/> instance containing the event data.
    /// </param>
    /// <remarks>
    /// This method notifies the server control that it should perform actions common to each HTTP
    /// request for the page it is associated with, such as setting up a database query. At this
    /// stage in the page lifecycle, server controls in the hierarchy are created and initialized,
    /// view state is restored, and form controls reflect client-side data. Use the IsPostBack
    /// property to determine whether the page is being loaded in response to a client postback,
    /// or if it is being loaded and accessed for the first time.
    /// </remarks>
    protected override void OnLoad(EventArgs e)
    {
      Assert.ArgumentNotNull(e, "e");
      Assert.CanRunApplication("/sitecore/content/Applications/Control Panel/Globalization/Import Language");

      base.OnLoad(e);

      if (!Context.ClientPage.IsEvent)
      {
        this.LanguageFile.Value = Registry.GetString("/Current_User/Import Languages/File");
      }
      var databases = Factory.GetDatabaseNames();
      foreach (var db in databases)
      {
        var database = Client.GetDatabaseNotNull(db);

        if (database.ReadOnly)
        {
          continue;
        }
        var option = new ListItem
        {
          Header = db,
          Value = db
        };
        this.Databases.Controls.Add(option);
        option.Selected = db.Equals("core", StringComparison.InvariantCultureIgnoreCase);
      }
      this.WizardCloseConfirmationText = Texts.AreYouSureYouWantToCancelTheLanguageImport;
    }

    /// <summary>
    /// Starts the import.
    /// </summary>
    protected void StartImport()
    {
      Registry.SetString("/Current_User/Import Languages/File", this.LanguageFile.Value);

      List<string> languages = GetSelectedLanguageNames();

      JobOptions jobOptions = new JobOptions(
        "ImportLanguage",
        "ImportLanguage",
        Client.Site.Name,
        new Importer(this.Databases.SelectedItem.Value, this.LanguageFile.Value, languages),
        "Import")
      {
        ContextUser = Context.User
      };

      jobOptions.AfterLife = TimeSpan.FromMinutes(1);
      jobOptions.WriteToLog = false;
      Job job = JobManager.Start(jobOptions);

      Context.ClientPage.ServerProperties["handle"] = job.Handle.ToString();
      SheerResponse.Timer("CheckStatus", 500);
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Gets the display name of the language.
    /// </summary>
    /// <param name="languageName">
    /// Name of the language.
    /// </param>
    /// <returns>
    /// The language display name.
    /// </returns>
    [NotNull]
    private static string GetLanguageDisplayName([NotNull] string languageName)
    {
      Assert.ArgumentNotNull(languageName, "languageName");

      string result;

      try
      {
        CultureInfo cultureInfo = Language.CreateCultureInfo(languageName);

        result = Language.GetDisplayName(cultureInfo);
      }
      catch
      {
        result = languageName;
      }

      return result;
    }

    /// <summary>
    /// Gets the selected language names.
    /// </summary>
    /// <returns>
    /// The selected language names.
    /// </returns>
    [NotNull]
    private static List<string> GetSelectedLanguageNames()
    {
      List<string> result = new List<string>();

      foreach (string key in HttpContext.Current.Request.Form.Keys)
      {
        if (string.IsNullOrEmpty(key))
        {
          continue;
        }

        if (!key.StartsWith("SelectedLanguage", StringComparison.InvariantCulture))
        {
          continue;
        }

        string languageName = HttpContext.Current.Request.Form[key];

        result.Add(languageName);
      }

      return result;
    }

    /// <summary>
    /// Gets the selected languages.
    /// </summary>
    /// <returns>
    /// The selected languages.
    /// </returns>
    [NotNull]
    private static List<string> GetSelectedLanguageDisplayNames()
    {
      List<string> result = new List<string>();

      foreach (string key in HttpContext.Current.Request.Form.Keys)
      {
        if (string.IsNullOrEmpty(key))
        {
          continue;
        }

        if (!key.StartsWith("SelectedLanguage", StringComparison.InvariantCulture))
        {
          continue;
        }

        string languageName = HttpContext.Current.Request.Form[key];

        string displayName = GetLanguageDisplayName(languageName);

        result.Add(displayName);
      }

      return result;
    }

    /// <summary>
    /// Renders the info.
    /// </summary>
    private void RenderInfo()
    {
      this.LanguageFileInfo.Text = this.LanguageFile.Value;

      List<string> languages = GetSelectedLanguageDisplayNames();

      HtmlTextWriter selectedLanguages = new HtmlTextWriter(new StringWriter());

      foreach (string language in languages)
      {
        selectedLanguages.Write("<div>");
        selectedLanguages.Write(language);
        selectedLanguages.Write("</div>");
      }

      this.SelectedLanguages.InnerHtml = selectedLanguages.InnerWriter.ToString();
    }

    /// <summary>
    /// Renders the languages.
    /// </summary>
    /// <param name="filename">
    /// The filename.
    /// </param>
    /// <returns>
    /// The render languages.
    /// </returns>
    private bool RenderLanguages([NotNull] string filename)
    {
      Assert.ArgumentNotNull(filename, "filename");

      XmlDocument doc;

      try
      {
        doc = XmlUtil.LoadXmlFile(filename);
      }
      catch (XmlException)
      {
        return false;
      }

      XmlNodeList nodes = doc.SelectNodes("/sitecore/phrase");
      if (nodes == null)
      {
        return false;
      }

      XmlNode phrase = null;

      foreach (XmlNode node in nodes)
      {
        if (node.HasChildNodes)
        {
          phrase = node;
          break;
        }
      }

      if (phrase == null)
      {
        return false;
      }

      HtmlTextWriter checklist = new HtmlTextWriter(new StringWriter());

      int index = 0;

      foreach (XmlNode node in phrase.ChildNodes)
      {
        string languageName = node.LocalName;

        if (string.IsNullOrEmpty(languageName))
        {
          continue;
        }

        string displayName = GetLanguageDisplayName(languageName);

        checklist.Write("<div>");
        checklist.Write(
          "<input id=\"SelectedLanguage" + index + "\" type=\"checkbox\" checked=\"checked\" value=\"" + languageName +
          "\"/>");
        checklist.Write(displayName);
        checklist.Write("</div>");

        index++;
      }

      this.LanguageList.InnerHtml = checklist.InnerWriter.ToString();

      return true;
    }

    #endregion

    #region Embedded class - Importer

    /// <summary>
    /// The importer.
    /// </summary>
    public class Importer
    {
      #region Fields

      /// <summary>
      /// The _database name.
      /// </summary>
      private readonly string databaseName;

      /// <summary>
      /// The filename.
      /// </summary>
      private readonly string filename;

      /// <summary>
      /// The languages.
      /// </summary>
      private readonly List<string> languages;

      /// <summary>
      /// The _folder template.
      /// </summary>
      private TemplateItem folderTemplate;

      /// <summary>
      /// The root item.
      /// </summary>
      private Item root;

      /// <summary>
      /// The template.
      /// </summary>
      private TemplateItem template;

      /// <summary>
      /// Indicates whether workflow should be disabled during import.
      /// </summary>
      private bool disableWorkflow;

      /// <summary>
      /// Indicates whether events should be disabled during the import.
      /// </summary>
      private bool disableEvents;

      /// <summary>
      /// The context database.
      /// </summary>
      private Database contextDatabase;

      /// <summary>
      /// Cache of dictionary sections.
      /// </summary>
      private readonly Dictionary<ID, Dictionary<string, DictionarySection>> sectionDictionaries = new Dictionary<ID, Dictionary<string, DictionarySection>>();

      #endregion

      #region Constructor

      /// <summary>
      /// Initializes a new instance of the <see cref="Importer"/> class.
      /// </summary>
      /// <param name="databaseName">
      /// Name of the database.
      /// </param>
      /// <param name="filename">
      /// The filename.
      /// </param>
      /// <param name="languages">
      /// The languages.
      /// </param>
      public Importer([NotNull] string databaseName, [NotNull] string filename, [NotNull] List<string> languages)
        : this(databaseName, filename, languages, false)
      {
        Assert.ArgumentNotNullOrEmpty(databaseName, "databaseName");
        Assert.ArgumentNotNullOrEmpty(filename, "filename");
        Assert.ArgumentNotNull(languages, "languages");
        Assert.IsTrue(languages.Count > 0, "No languages to import");
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="Importer"/> class.
      /// </summary>
      /// <param name="databaseName">Name of the database.</param>
      /// <param name="filename">The filename.</param>
      /// <param name="languages">The languages.</param>
      /// <param name="disableWorkflow">if set to <c>true</c> workflow should be disabled during import.</param>
      public Importer([NotNull] string databaseName, [NotNull] string filename, [NotNull] List<string> languages, bool disableWorkflow)
        : this(databaseName, filename, languages, disableWorkflow, false)
      {
        Assert.ArgumentNotNullOrEmpty(databaseName, "databaseName");
        Assert.ArgumentNotNullOrEmpty(filename, "filename");
        Assert.ArgumentNotNull(languages, "languages");
        Assert.IsTrue(languages.Count > 0, "No languages to import");
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="Importer"/> class.
      /// </summary>
      /// <param name="databaseName">Name of the database.</param>
      /// <param name="filename">The filename.</param>
      /// <param name="languages">The languages.</param>
      /// <param name="disableWorkflow">if set to <c>true</c> workflow should be disabled during import.</param>
      /// <param name="disableEvents">if set to <c>true</c> events should be disabled during import.</param>
      public Importer([NotNull] string databaseName, [NotNull] string filename, [NotNull] List<string> languages, bool disableWorkflow, bool disableEvents)
      {
        Assert.ArgumentNotNullOrEmpty(databaseName, "databaseName");
        Assert.ArgumentNotNullOrEmpty(filename, "filename");
        Assert.ArgumentNotNull(languages, "languages");
        Assert.IsTrue(languages.Count > 0, "No languages to import");

        this.filename = filename;
        this.languages = languages;
        this.databaseName = databaseName;
        this.disableWorkflow = disableWorkflow;
        this.disableEvents = disableEvents;
      }

      #endregion

      /// <summary>
      /// Gets the database.
      /// </summary>
      /// <value>The database.</value>
      [NotNull]
      protected Database Database
      {
        get
        {
          if (this.contextDatabase == null)
          {
            this.contextDatabase = Factory.GetDatabase(this.databaseName);
          }

          return Assert.ResultNotNull(this.contextDatabase);
        }
      }

      #region Protected methods

      /// <summary>
      /// Imports this instance.
      /// </summary>
      protected void Import()
      {
        Job job = Context.Job;
        if (job == null)
        {
          return;
        }

        try
        {
          if (this.disableWorkflow)
          {
            Workflows.WorkflowContextStateSwitcher.Enter(Workflows.WorkflowContextState.Disabled);
          }

          if (this.disableEvents)
          {
            EventDisabler.Enter(EventDisablerState.Enabled);
          }

          DictionaryBatchOperationContext.Enter(true);

          Database database = Factory.GetDatabase(this.databaseName);
          Assert.IsNotNull(database, "Database \"{0}\" not found", this.databaseName);

          this.root = database.GetItem("/sitecore/system/dictionary");
          Error.AssertItem(this.root, "/sitecore/system/dictionary");

          this.folderTemplate = database.Templates["System/Dictionary/Dictionary folder"];
          Error.AssertTemplate(this.folderTemplate, "Dictionary folder");

          this.template = database.Templates["System/Dictionary/Dictionary entry"];

          XmlDocument doc = XmlUtil.LoadXmlFile(this.filename);

          XmlNodeList list = doc.SelectNodes("/sitecore/phrase");
          if (list == null)
          {
            return;
          }

          job.Status.Total = list.Count;

          foreach (XmlNode phrase in list)
          {
            this.ImportPhrase(job, database, phrase);

            job.Status.Processed++;
          }

          Log.Audit(this, "Import language: {0}, from: {1}", StringUtil.Join(this.languages, ", "), this.filename);

        }
        catch (Exception ex)
        {
          job.Status.Failed = true;
          job.Status.LogException(ex);
        }
        finally
        {
          if (this.disableWorkflow)
          {
            try
            {
              Workflows.WorkflowContextStateSwitcher.Exit();
            }
            catch
            {
            }
          }

          if (this.disableEvents)
          {
            try
            {
              EventDisabler.Exit();
            }
            catch
            {
            }
          }

          DictionaryBatchOperationContext.Exit();
          Translate.ResetCache(true);
        }

        job.Status.State = JobState.Finished;
      }

      /// <summary>
      /// Imports the phrase.
      /// </summary>
      /// <param name="job">
      /// The own job.
      /// </param>
      /// <param name="database">
      /// The database.
      /// </param>
      /// <param name="phrase">
      /// The phrase.
      /// </param>
      protected void ImportPhrase([NotNull] Job job, [NotNull] Database database, [NotNull] XmlNode phrase)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(database, "database");
        Assert.ArgumentNotNull(phrase, "phrase");

        string itemID = XmlUtil.GetAttribute("itemid", phrase);
        string fieldID = XmlUtil.GetAttribute("fieldid", phrase);
        string databaseNameParsed = XmlUtil.GetAttribute("database", phrase);
        Database phraseDatabase = database;
        if (!string.IsNullOrEmpty(databaseNameParsed) && string.Compare(databaseNameParsed, database.Name, StringComparison.InvariantCultureIgnoreCase) != 0)
        {
          try
          {
            phraseDatabase = Factory.GetDatabase(databaseNameParsed);
          }
          catch
          {
          }
        }

        if (itemID.Length > 0 && fieldID.Length > 0)
        {
          this.UpdateItem(job, phraseDatabase, itemID, fieldID, phrase);
        }
        else
        {
          this.UpdateDictionary(job, phrase);
        }
      }

      /// <summary>
      /// Tries the create persistent section.
      /// </summary>
      /// <param name="dictionaryRoot">The dictionary root.</param>
      /// <param name="sectionName">Name of the section.</param>
      /// <param name="folderTemplateItem">The template that will be used fro new item.</param>
      /// <returns>
      /// Item that represens dictionary section folder.
      /// </returns>
      protected virtual Item TryCreatePersistentSection(Item dictionaryRoot, string sectionName, TemplateItem folderTemplateItem)
      {
        Item sectionItem = null;
        try
        {
          string sectionPath = string.Format("{0}/{1}", dictionaryRoot.Paths.FullPath, sectionName);
          sectionPath = sectionPath.ToLowerInvariant();
          ID sectionId = MainUtil.GetMD5Hash(sectionPath);
          sectionItem = dictionaryRoot.Database.GetItem(sectionId);
          if (sectionItem == null)
          {
            //Item with generated ID does not exists. We can safely create it.
            sectionItem = folderTemplateItem.AddTo(dictionaryRoot, sectionName, sectionId);
          }
          else
          {
            if (!sectionItem.Paths.LongID.ToLowerInvariant().Contains(dictionaryRoot.Paths.LongID.ToLowerInvariant()))
            {
              //Item with specified ID already exists but not under the dictionery root.
              //We need to create item with new ID under the root.
              sectionItem = null;
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error(string.Format("Cannot create new dictionary folder '{0}' for dictionary '{1}'", sectionName, dictionaryRoot.Paths.FullPath), ex, this);
          sectionItem = null;
        }

        return sectionItem;
      }

      /// <summary>
      /// Tries the create persistent phrase.
      /// </summary>
      /// <param name="sectionItem">The section item.</param>
      /// <param name="itemName">Name of the item.</param>
      /// <param name="key">The phrase key.</param>
      /// <param name="phraseTemplateItem">The phrase template item.</param>
      /// <returns>Dictionary Phrase item.</returns>
      protected virtual Item TryCreatePersistentPhrase(Item sectionItem, string itemName, string key, TemplateItem phraseTemplateItem)
      {
        Item dictionaryPhrase = null;
        try
        {
          string phraseUniqueString = string.Format("{0}/{1}", sectionItem.Paths.FullPath, key);
          phraseUniqueString = phraseUniqueString.ToLowerInvariant();
          ID phraseId = MainUtil.GetMD5Hash(phraseUniqueString);
          dictionaryPhrase = sectionItem.Database.GetItem(phraseId);
          if (dictionaryPhrase == null)
          {
            //Item with generated ID does not exists. We can create it.
            dictionaryPhrase = phraseTemplateItem.AddTo(sectionItem, itemName, phraseId);
          }
          else
          {
            if (!dictionaryPhrase.Paths.LongID.ToLowerInvariant().Contains(sectionItem.Paths.LongID.ToLowerInvariant()) || !SameKeys(dictionaryPhrase["key"], key))
            {
              //Item with specified ID already exists but not under the dictionery root.
              //We need to createt item with new ID under the root.
              dictionaryPhrase = null;
            }
          }
        }
        catch (Exception ex)
        {
          Log.Error(string.Format("Cannot create new dictionary phrase item '{0}' for section'{1}'", itemName, sectionItem.Paths.FullPath), ex, this);
          dictionaryPhrase = null;
        }

        return dictionaryPhrase;
      }

      /// <summary>
      /// Updates the dictionary.
      /// </summary>
      /// <param name="job">
      /// The context job.
      /// </param>
      /// <param name="phrase">
      /// The phrase.
      /// </param>
      protected void UpdateDictionary([NotNull] Job job, [NotNull] XmlNode phrase)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(phrase, "phrase");

        string key = XmlUtil.GetAttribute("key", phrase);

        // find key
        string itemName = key;

        if (itemName.Length == 0)
        {
          itemName = XmlUtil.GetChildValue("en", phrase);
        }

        if (itemName.Length == 0)
        {
          job.Status.LogError("Missing key in \"" + phrase.OuterXml + "\"");
          return;
        }

        itemName = StringUtil.Left(itemName.Trim(), 100);

        itemName = Regex.Replace(itemName, "\\W", " ").Trim();

        itemName = ItemUtil.ProposeValidItemName(itemName);

        string itemNameError = ItemUtil.GetItemNameError(itemName);
        if (itemNameError.Length > 0)
        {
          job.Status.LogError(itemNameError + " in \"" + phrase.OuterXml + "\"");
          return;
        }

        var dictionaryRoot = this.root;
        string dictionaryDomain = XmlUtil.GetAttribute("domain", phrase);
        if (!string.IsNullOrEmpty(dictionaryDomain))
        {
          DictionaryDomain domain;
          if (!DictionaryDomain.TryParse(dictionaryDomain, this.Database, out domain) || domain == null)
          {
            job.Status.LogError(string.Format("Dictionary domain \"{0}\" not found.", dictionaryDomain));
            return;
          }

          dictionaryRoot = domain.GetDefinitionItem();
          if (dictionaryRoot == null)
          {
            job.Status.LogError(string.Format("Dictionary item for domain \"{0}\" not found.", dictionaryDomain));
            return;
          }
        }

        // find folder
        string sectionName = StringUtil.Left(itemName, 1).ToUpperInvariant();
        if (string.IsNullOrEmpty(sectionName))
        {
          return;
        }

        Dictionary<string, DictionarySection> targetSectionsDictionary;

        if (!this.sectionDictionaries.TryGetValue(dictionaryRoot.ID, out targetSectionsDictionary))
        {
          targetSectionsDictionary = new Dictionary<string, DictionarySection>();
          this.sectionDictionaries.Add(dictionaryRoot.ID, targetSectionsDictionary);
        }

        DictionarySection section;

        if (!targetSectionsDictionary.TryGetValue(sectionName, out section))
        {
          Item sectionItem = dictionaryRoot.Children[sectionName];
          if (sectionItem == null)
          {
            sectionItem = this.TryCreatePersistentSection(dictionaryRoot, sectionName, this.folderTemplate);
            if (sectionItem == null)
            {
              sectionItem = this.folderTemplate.AddTo(dictionaryRoot, sectionName);
            }
          }

          section = new DictionarySection(sectionItem);
          targetSectionsDictionary[sectionName] = section;
        }

        // find entry
        Item entry = section.GetEntry(itemName);
        if (entry == null)
        {
          entry = this.TryCreatePersistentPhrase(section.Item, itemName, key, this.template);
          if (entry == null)
          {
            entry = this.template.AddTo(section.Item, itemName);
          }

          section.AddEntry(entry);
        }
        else if (!string.IsNullOrEmpty(entry["key"]) && entry["key"] != key)
        {
          entry = ResolveDuplicateItemNames(entry, key, this.template);
        }

        entry.Editing.BeginEdit();
        entry["Key"] = key;
        entry.Editing.EndEdit();

        this.Update(job, entry, "Phrase", phrase);
      }

      /// <summary>
      /// Updates the item.
      /// </summary>
      /// <param name="job">
      /// The job that performs Import.
      /// </param>
      /// <param name="database">
      /// The database.
      /// </param>
      /// <param name="itemID">
      /// The item ID.
      /// </param>
      /// <param name="fieldID">
      /// The field ID.
      /// </param>
      /// <param name="phrase">
      /// The phrase.
      /// </param>
      protected void UpdateItem(
        [NotNull] Job job,
        [NotNull] Database database,
        [NotNull] string itemID,
        [NotNull] string fieldID,
        [NotNull] XmlNode phrase)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(database, "database");
        Assert.ArgumentNotNullOrEmpty(itemID, "itemID");
        Assert.ArgumentNotNullOrEmpty(fieldID, "fieldID");
        Assert.ArgumentNotNull(phrase, "phrase");

        Item item = database.GetItem(itemID);

        if (item != null)
        {
          this.Update(job, item, fieldID, phrase);
        }
        else
        {
          string path = XmlUtil.GetAttribute("path", phrase);

          if (path.StartsWith("/sitecore/system/Dictionary/", StringComparison.InvariantCulture))
          {
            this.UpdateDictionary(job, phrase);
            return;
          }

          job.Status.LogError(Translate.Text(Texts.ITEM_0_NOT_FOUND, itemID));
        }
      }

      #endregion

      #region Private methods

      /// <summary>
      /// Resolves the duplicate item names.
      /// </summary>
      /// <param name="entry">The entry.</param>
      /// <param name="key">The phrase key.</param>
      /// <param name="template">The template.</param>
      /// <returns>
      /// The duplicate item names.
      /// </returns>
      [NotNull]
      private Item ResolveDuplicateItemNames([NotNull]Item entry, [NotNull]string key, [CanBeNull]TemplateItem template)
      {
        Assert.ArgumentNotNull(entry, "entry");
        Assert.ArgumentNotNull(key, "key");

        int counter = 0;
        Item item = entry;

        string calculatedName = string.Empty;
        while (item != null && !SameKeys(item["key"], key))
        {
          counter++;
          calculatedName = CalculateName(entry.Name, counter);
          if (item.Parent != null)
          {
            item = item.Parent.Children[calculatedName];
          }
        }

        if (item == null)
        {
          Assert.IsNotNull(template, "template");
          item = this.TryCreatePersistentPhrase(entry.Parent, calculatedName, key, template);
          if (item == null)
          {
            item = template.AddTo(entry.Parent, calculatedName);
          }
        }

        return Assert.ResultNotNull(item);
      }

      /// <summary>Calculates name with counter postfix.</summary>
      /// <param name="name">The name.</param>
      /// <param name="counter">The counter.</param>
      /// <returns>The name with counted in postfix.</returns>
      private static string CalculateName(string name, int counter)
      {
        var postFix = "_" + counter;
        var result = name + postFix;
        if (result.Length > Settings.MaxItemNameLength)
        {
          result = name.Substring(0, name.Length - (result.Length - Settings.MaxItemNameLength)) + postFix;
        }

        return result;
      }

      /// <summary>
      /// Checks if the two strings represent the same key.
      /// </summary>
      /// <param name="itemkey">The item key.</param>
      /// <param name="key">The key from translation file.</param>
      /// <returns>
      ///   <c>true</c> if the two parametes represent the same key value.
      /// </returns>
      private static bool SameKeys([NotNull]string itemkey, [NotNull]string key)
      {
        Assert.ArgumentNotNull(itemkey, "itemkey");
        Assert.ArgumentNotNull(key, "key");

        return itemkey.Replace("\r\n", "\n") == key.Replace("\r\n", "\n");
      }

      /// <summary>
      /// Updates the specified job.
      /// </summary>
      /// <param name="job">
      /// The own job.
      /// </param>
      /// <param name="entry">
      /// The entry.
      /// </param>
      /// <param name="fieldID">
      /// The field ID.
      /// </param>
      /// <param name="phrase">
      /// The phrase.
      /// </param>
      private void Update([NotNull] Job job, [NotNull] Item entry, [NotNull] string fieldID, [NotNull] XmlNode phrase)
      {
        Assert.ArgumentNotNull(job, "job");
        Assert.ArgumentNotNull(entry, "entry");
        Assert.ArgumentNotNullOrEmpty(fieldID, "fieldID");
        Assert.ArgumentNotNull(phrase, "phrase");

        foreach (XmlNode languageNode in phrase.ChildNodes)
        {
          if (languageNode.NodeType != XmlNodeType.Element)
          {
            job.Status.LogError("Invalid entry in file at " + languageNode.OuterXml);
            continue;
          }

          string languageName = languageNode.LocalName;
          if (!this.languages.Contains(languageName))
          {
            continue;
          }

          Item item = entry.Database.GetItem(entry.ID, Language.Parse(languageName));
          if (item == null)
          {
            continue;
          }

          Field field = item.Fields[fieldID];
          if (field == null)
          {
            continue;
          }

          if (!field.ShouldBeTranslated)
          {
            continue;
          }

          string text = XmlUtil.GetValue(languageNode);
          
          item.Editing.BeginEdit();
          field.SetValue(text, true);
          item.Editing.EndEdit();          
        }
      }

      #endregion

      #region Nested class - Dictionary Section

      /// <summary>
      /// Caches child list of a single dictionary section.
      /// </summary>
      private class DictionarySection
      {
        private Item item;
        private Dictionary<string, Item> entries = new Dictionary<string, Item>();

        public DictionarySection(Item item)
        {
          this.item = item;
          foreach (Item child in new ChildList(this.item, ChildListOptions.IgnoreSecurity | ChildListOptions.SkipSorting))
          {
            this.entries[child.Key] = child;
          }
        }

        public Item Item
        {
          get
          {
            return this.item;
          }
        }

        public Item GetEntry(string name)
        {
          Item result;
          if (this.entries.TryGetValue(name.ToLowerInvariant(), out result))
          {
            return result;
          }

          return null;
        }

        public void AddEntry(Item child)
        {
          this.entries[child.Key] = child;
        }
      }

      #endregion
    }

    #endregion
  }
}