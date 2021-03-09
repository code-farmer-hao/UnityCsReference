// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Linq;
using UnityEditor.Scripting.ScriptCompilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.PackageManager.UI.Internal
{
    internal class PackageManagerWindowRoot : VisualElement, IWindow
    {
        private string m_PackageToSelectOnLoaded;

        private PackageFilterTab? m_FilterToSelectAfterLoad;

        private ResourceLoader m_ResourceLoader;
        private ExtensionManager m_ExtensionManager;
        private SelectionProxy m_Selection;
        private PackageFiltering m_PackageFiltering;
        private PackageManagerPrefs m_PackageManagerPrefs;
        private PackageDatabase m_PackageDatabase;
        private PageManager m_PageManager;
        private PackageManagerProjectSettingsProxy m_SettingsProxy;
        private UnityConnectProxy m_UnityConnectProxy;
        private ApplicationProxy m_ApplicationProxy;
        private UpmClient m_UpmClient;
        private void ResolveDependencies(ResourceLoader resourceLoader,
            ExtensionManager extensionManager,
            SelectionProxy selection,
            PackageFiltering packageFiltering,
            PackageManagerPrefs packageManagerPrefs,
            PackageDatabase packageDatabase,
            PageManager pageManager,
            PackageManagerProjectSettingsProxy settingsProxy,
            UnityConnectProxy unityConnectProxy,
            ApplicationProxy applicationProxy,
            UpmClient upmClient)
        {
            m_ResourceLoader = resourceLoader;
            m_ExtensionManager = extensionManager;
            m_Selection = selection;
            m_PackageFiltering = packageFiltering;
            m_PackageManagerPrefs = packageManagerPrefs;
            m_PackageDatabase = packageDatabase;
            m_PageManager = pageManager;
            m_SettingsProxy = settingsProxy;
            m_UnityConnectProxy = unityConnectProxy;
            m_ApplicationProxy = applicationProxy;
            m_UpmClient = upmClient;
        }

        public PackageManagerWindowRoot(ResourceLoader resourceLoader,
                                        ExtensionManager extensionManager,
                                        SelectionProxy selection,
                                        PackageFiltering packageFiltering,
                                        PackageManagerPrefs packageManagerPrefs,
                                        PackageDatabase packageDatabase,
                                        PageManager pageManager,
                                        PackageManagerProjectSettingsProxy settingsProxy,
                                        UnityConnectProxy unityConnectProxy,
                                        ApplicationProxy applicationProxy,
                                        UpmClient upmClient)
        {
            ResolveDependencies(resourceLoader, extensionManager, selection, packageFiltering, packageManagerPrefs, packageDatabase, pageManager, settingsProxy, unityConnectProxy, applicationProxy, upmClient);

            styleSheets.Add(m_ResourceLoader.packageManagerWindowStyleSheet);

            var root = m_ResourceLoader.GetTemplate("PackageManagerWindow.uxml");
            Add(root);
            cache = new VisualElementCache(root);
        }

        public void OnEnable()
        {
            packageDetails.OnEnable();
            packageList.OnEnable();
            packageLoadBar.OnEnable();
            packageManagerToolbar.OnEnable();
            packageStatusbar.OnEnable();

            packageManagerToolbar.SetEnabled(!m_PackageDatabase.isEmpty);
            packageDetails.packageToolbarContainer.SetEnabled(!m_PackageDatabase.isEmpty);

            leftColumnContainer.style.flexGrow = m_PackageManagerPrefs.splitterFlexGrow;
            rightColumnContainer.style.flexGrow = 1 - m_PackageManagerPrefs.splitterFlexGrow;

            m_PageManager.onRefreshOperationFinish += OnRefreshOperationFinish;
            m_PageManager.onRefreshOperationStart += OnRefreshOperationStart;
            m_PageManager.onRefreshOperationError += OnRefreshOperationError;
            m_PackageFiltering.onFilterTabChanged += OnFilterChanged;

            m_UnityConnectProxy.onUserLoginStateChange += OnUserLoginStateChange;

            PackageManagerWindowAnalytics.Setup();

            var newTab = m_PackageManagerPrefs.lastUsedPackageFilter ?? m_PackageManagerPrefs.defaultFilterTab;
            m_PackageFiltering.SetCurrentFilterTabWithoutNotify(newTab);
            packageManagerToolbar.SetFilter(newTab);

            if (newTab != PackageFilterTab.AssetStore)
                UIUtils.SetElementDisplay(packageLoadBar, false);

            if (m_PageManager.GetRefreshTimestamp(newTab) == 0)
                DelayRefresh(newTab);

            if (newTab != PackageFilterTab.UnityRegistry && m_PageManager.GetRefreshTimestamp(PackageFilterTab.UnityRegistry) == 0 && m_ApplicationProxy.isUpmRunning)
                DelayRefresh(PackageFilterTab.UnityRegistry);

            EditorApplication.focusChanged += OnFocusChanged;
            m_Selection.onSelectionChanged += RefreshSelectedInInspectorClass;

            focusable = true;
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            RefreshSelectedInInspectorClass();

            m_ExtensionManager.OnWindowCreated(this, packageDetails.extensionContainer);
        }

        private void DelayRefresh(PackageFilterTab tab)
        {
            if (!m_ApplicationProxy.isUpmRunning)
            {
                if (!m_ApplicationProxy.isBatchMode)
                    Debug.Log(L10n.Tr("[Package Manager Window] UPM server is not running. Please check that your Editor was not launched with '-noUpm' command line option."));

                packageList.HidePackagesShowMessage(false, false, L10n.Tr("UPM server is not running"));
                packageStatusbar.DisableRefresh();
                packageManagerToolbar.SetEnabled(false);
                return;
            }

            if (m_PackageManagerPrefs.numItemsPerPage == null ||
                tab == PackageFilterTab.AssetStore && !m_UnityConnectProxy.isUserInfoReady)
            {
                EditorApplication.delayCall += () => DelayRefresh(tab);
                return;
            }

            m_PageManager.Refresh(tab, (int)m_PackageManagerPrefs.numItemsPerPage);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RegisterCallback<ValidateCommandEvent>(OnValidateCommandEvent);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommandEvent);
            packageList.Focus();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            UnregisterCallback<ValidateCommandEvent>(OnValidateCommandEvent);
            UnregisterCallback<ExecuteCommandEvent>(OnExecuteCommandEvent);
        }

        private void OnValidateCommandEvent(ValidateCommandEvent evt)
        {
            if (evt.commandName == EventCommandNames.Find)
                evt.StopPropagation();
        }

        private void OnExecuteCommandEvent(ExecuteCommandEvent evt)
        {
            if (evt.commandName == EventCommandNames.Find)
            {
                packageManagerToolbar.FocusOnSearch();
                evt.StopPropagation();
            }
        }

        private void OnFocusChanged(bool focus)
        {
            var canRefresh = !EditorApplication.isPlaying && !EditorApplication.isCompiling;
            if (focus && canRefresh && m_PackageFiltering.currentFilterTab == PackageFilterTab.AssetStore)
                m_PageManager.Refresh(RefreshOptions.PurchasedOffline, m_PackageManagerPrefs.numItemsPerPage ?? PageManager.k_DefaultPageSize);
        }

        public void OnDisable()
        {
            m_PackageManagerPrefs.lastUsedPackageFilter = m_PackageFiltering.currentFilterTab;

            m_PageManager.onRefreshOperationFinish -= OnRefreshOperationFinish;
            m_PageManager.onRefreshOperationStart -= OnRefreshOperationStart;
            m_PageManager.onRefreshOperationError -= OnRefreshOperationError;
            m_PackageFiltering.onFilterTabChanged -= OnFilterChanged;

            m_UnityConnectProxy.onUserLoginStateChange -= OnUserLoginStateChange;

            packageDetails.OnDisable();
            packageList.OnDisable();
            packageLoadBar.OnDisable();
            packageManagerToolbar.OnDisable();
            packageStatusbar.OnDisable();

            EditorApplication.focusChanged -= OnFocusChanged;
            m_Selection.onSelectionChanged -= RefreshSelectedInInspectorClass;

            m_PackageManagerPrefs.splitterFlexGrow = leftColumnContainer.resolvedStyle.flexGrow;
        }

        private void OnUserLoginStateChange(bool userInfoReady, bool loggedIn)
        {
            if (!userInfoReady || m_PackageDatabase.isEmpty || !m_PageManager.IsInitialFetchingDone())
                return;

            var entitlements = m_PackageDatabase.allPackages.Where(package =>  package.hasEntitlements);
            if (loggedIn)
            {
                if (entitlements.Any(package => (package.versions?.primary.isInstalled ?? false) && (package.versions?.primary.hasEntitlementsError ?? false)))
                    m_UpmClient.Resolve();
                else
                {
                    m_PageManager.Refresh(RefreshOptions.UpmList | RefreshOptions.UpmSearch);
                    m_PageManager.RefreshSelected();
                }
            }
            else
            {
                if (entitlements.Any())
                {
                    m_PageManager.Refresh(RefreshOptions.UpmList | RefreshOptions.UpmSearch);
                    m_PageManager.RefreshSelected();
                }
            }
        }

        public void OnDestroy()
        {
            m_ExtensionManager.OnWindowDestroy();
        }

        private void OnRefreshOperationFinish()
        {
            packageManagerToolbar.SetEnabled(true);
            packageDetails.packageToolbarContainer.SetEnabled(true);

            if (m_FilterToSelectAfterLoad != null && m_PageManager.GetRefreshTimestamp(m_FilterToSelectAfterLoad) > 0)
                SelectPackageAndFilter();
        }

        private void OnFilterChanged(PackageFilterTab filterTab)
        {
            if (!filterTab.Equals(PackageFilterTab.AssetStore))
                UIUtils.SetElementDisplay(packageLoadBar, false);
            else
            {
                packageLoadBar.Refresh();
                UIUtils.SetElementDisplay(packageLoadBar, true);
            }

            DisableToolbarIfRefreshInProgress(filterTab);
        }

        private void SelectPackageAndFilter()
        {
            if (!m_PageManager.IsInitialFetchingDone())
            {
                return;
            }

            IPackageVersion version = null;
            IPackage package = null;
            if (!string.IsNullOrEmpty(m_PackageToSelectOnLoaded))
                m_PackageDatabase.GetPackageAndVersionByIdOrName(m_PackageToSelectOnLoaded, out package, out version);

            if (m_FilterToSelectAfterLoad == PackageFilterTab.AssetStore)
            {
                m_PackageFiltering.currentFilterTab = PackageFilterTab.AssetStore;

                if (!string.IsNullOrEmpty(m_PackageToSelectOnLoaded))
                {
                    if (package == null || package is PlaceholderPackage)
                        m_PageManager.Fetch(m_PackageToSelectOnLoaded);
                    else
                        m_PageManager.GetPage(PackageFilterTab.AssetStore).Load(package, version);
                }

                m_FilterToSelectAfterLoad = null;
                m_PackageToSelectOnLoaded = null;
                return;
            }

            if (package != null || m_FilterToSelectAfterLoad != null)
            {
                var tab = m_FilterToSelectAfterLoad ?? PackageFilterTab.UnityRegistry;

                m_PackageFiltering.currentFilterTab = tab;
                if (!string.IsNullOrEmpty(m_PackageToSelectOnLoaded))
                {
                    m_PageManager.SetSelected(package, version, true);
                    packageList.OnFocus();
                }

                m_FilterToSelectAfterLoad = null;
                m_PackageToSelectOnLoaded = null;
            }
        }

        public void OnFocus()
        {
            AddToClassList("focus");
        }

        public void OnLostFocus()
        {
            RemoveFromClassList("focus");
        }

        private void RefreshSelectedInInspectorClass()
        {
            if (m_Selection.activeObject is PackageSelectionObject)
                AddToClassList("selectedInInspector");
            else
                RemoveFromClassList("selectedInInspector");
        }

        private void OnRefreshOperationStart()
        {
            DisableToolbarIfRefreshInProgress();
        }

        private void DisableToolbarIfRefreshInProgress(PackageFilterTab? tab = null)
        {
            if (m_PageManager.IsRefreshInProgress(tab))
            {
                packageManagerToolbar.SetEnabled(false);
                packageDetails.packageToolbarContainer.SetEnabled(false);
            }
        }

        private void OnRefreshOperationError(UIError error)
        {
            packageManagerToolbar.SetEnabled(true);
            packageDetails.packageToolbarContainer.SetEnabled(true);
        }

        public void SelectPackageAndFilter(string packageToSelect, PackageFilterTab? filterTab = null, bool refresh = false, string searchText = "")
        {
            if (!string.IsNullOrEmpty(packageToSelect) || filterTab != null)
            {
                if (filterTab == null)
                {
                    m_PackageDatabase.GetPackageAndVersionByIdOrName(packageToSelect, out var package, out var version);
                    if (package != null)
                        filterTab = m_PageManager.FindTab(package, version);
                    else
                    {
                        var packageToSelectSplit = packageToSelect.Split('@');
                        var versionString = packageToSelectSplit.Length == 2 ? packageToSelectSplit[1] : string.Empty;

                        // Package is not found in PackageDatabase but we can determine if it's a preview package or not with it's version string.
                        SemVersionParser.TryParse(versionString, out var semVersion);
                        if (!m_SettingsProxy.enablePreReleasePackages && semVersion.HasValue && (semVersion.Value.Major == 0 || semVersion.Value.Prerelease.StartsWith("preview")))
                        {
                            Debug.Log("You must check \"Enable Preview Packages\" in Project Settings > Package Manager in order to see this package.");
                            filterTab = m_PackageFiltering.currentFilterTab;
                            packageToSelect = null;
                        }
                        else
                            filterTab = PackageFilterTab.UnityRegistry;
                    }
                }

                m_FilterToSelectAfterLoad = filterTab;
                m_PackageToSelectOnLoaded = packageToSelect;
                packageManagerToolbar.SetCurrentSearch(searchText);

                if (refresh || m_PackageDatabase.isEmpty)
                    DelayRefresh((PackageFilterTab)filterTab);
                else
                    SelectPackageAndFilter();
            }
        }

        public void OpenAddPackageByNameDropdown(string url)
        {
            var dropdown = new AddPackageByNameDropdown(m_ResourceLoader, m_PackageFiltering, m_UpmClient, m_PackageDatabase, m_PageManager, PackageManagerWindow.instance);

            var packageNameAndVersion = url.Replace(PackageManagerWindow.k_UpmUrl, string.Empty);
            var packageName = string.Empty;
            var packageVersion = string.Empty;

            if (packageNameAndVersion.Contains("@"))
            {
                var values = packageNameAndVersion.Split('@');
                if (values.Count() > 1)
                {
                    packageName = values[0];
                    packageVersion = values[1];
                }
            }
            else
                packageName = packageNameAndVersion;

            DropdownElement.ShowDropdown(this, dropdown);

            // We need to set the name and version after the dropdown is shown,
            // so that the OnTextFieldChange of placeholder gets called
            dropdown.packageNameField.value = packageName;
            dropdown.packageVersionField.value = packageVersion;
        }

        public IDetailsExtension AddDetailsExtension()
        {
            return m_ExtensionManager.CreateDetailsExtension();
        }

        public void Select(string identifier)
        {
            SelectPackageAndFilter(identifier);
        }

        public PackageSelectionArgs activeSelection
        {
            get
            {
                m_PageManager.GetSelectedPackageAndVersion(out var package, out var packageVersion);
                return new PackageSelectionArgs { package = package, packageVersion = packageVersion, window = this };
            }
        }

        private VisualElementCache cache { set; get; }
        internal PackageList packageList { get { return cache.Get<PackageList>("packageList"); } }
        internal PackageLoadBar packageLoadBar { get { return cache.Get<PackageLoadBar>("packageLoadBar"); } }
        private PackageDetails packageDetails { get { return cache.Get<PackageDetails>("packageDetails"); } }
        private PackageManagerToolbar packageManagerToolbar { get { return cache.Get<PackageManagerToolbar>("topMenuToolbar"); } }
        private PackageStatusBar packageStatusbar { get { return cache.Get<PackageStatusBar>("packageStatusBar"); } }
        private VisualElement leftColumnContainer { get { return cache.Get<VisualElement>("leftColumnContainer"); } }
        private VisualElement rightColumnContainer { get { return cache.Get<VisualElement>("rightColumnContainer"); } }
    }
}