using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Security.Principal;
using System.DirectoryServices; // For Converting Owner to UPN format
using System.Reflection; // For DoubleBuffer
using Peter; // For shell context menu
using System.Diagnostics;
using CubicOrange.Windows.Forms.ActiveDirectory;


namespace WindowsFormsApplication1
{
    public partial class Form1 : Form
    {
        // Variables
        private ListViewColumnSorter lvwColumnSorter; // For sorting by column
        private List<ListViewItem> listView1List = new List<ListViewItem>(); // List that will load into the listview
        DateTime lastRefreshGuiLabelsTime = new DateTime(); // Used to allow delay in updating GUI progress labels
        
        public int SetOwnerErrorCount { get; private set; }
        public int SearchErrorCount { get; private set; }
        public SizeUnit DefaultSizeUnit = SizeUnit.B;

        string strlogging = "###################" + Environment.NewLine + "File Searcher By Owner Log" + Environment.NewLine + "###################" + Environment.NewLine;
        FormErrorLog fLog;

        // Can't work with selected listview items directly so need to use this to use in the backgroundworker
        public delegate List<string> GetPathList();
        public List<string> GetPaths()
        {
            List<string> pathList = new List<string>();
            foreach (ListViewItem item in listView1.SelectedItems)
            {
                pathList.Add(item.Text);
            }
            return pathList;
        }

        // Used to reduce how much RAM is allocated (.NET does fine DYNAMIC allocation, but some users may look at task manager and go ape)
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        public Form1()
        {
            InitializeComponent();

            //Hide features not yet implemented
            //this.toolStripStatusLabel4.Visible = false;
            //this.toolStripDropDownButton1.Visible = false;
            //this.toolStripButton2.Visible = false;
            this.toolStripStatusLabel6.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.None; // Hide the border line to the right of the Owner button (normally it's Left only)
            
            // Create an instance of a ListView column sorter and assign it 
            // to the ListView control.
            lvwColumnSorter = new ListViewColumnSorter();
            this.listView1.ListViewItemSorter = lvwColumnSorter;
 
            // Sets the listView1 protected property Control.Double­Buffered to true to avoid flickering
            // Note: Causes slight performance hit when redrawing (such as when resizing or scrolling)
            SetDoubleBuffered(listView1);

            // Test making controls OPAQUE, but didn't really help the listview load issue
            // SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);

            this.columnHeaderSize.Text = "Size (" + DefaultSizeUnit.Name + "s)"; // Add "s" to make plural

            // Compensate for different OS working set widths (e.g., Windows 7 verses XP)
            this.listView1.Width = this.ClientSize.Width;
            this.statusStrip2.Width = this.ClientSize.Width;
            this.columnHeaderSize.Width = this.listView1.ClientSize.Width - (columnHeaderPath.Width + columnHeaderFilename.Width);

            // Add version to Form Text
            this.Text = "File Searcher by Owner (v" + Application.ProductVersion.ToString() +") " + "- The Grim Admin";

            // Set user as default owner and update SID Tooltip
            this.textBoxOwner.Text = SystemInformation.UserDomainName.ToString() + "\\" + SystemInformation.UserName.ToString() ;
            UpdateSIDTooltip();

            // Enable Disable UPN conversion link
            EnableDisableUPNTranslation();

            // Set default size measurement to Byte & Populate Dropdown. Set tag as SizeUnit
            toolStripSplitButton1.Text = DefaultSizeUnit.Name + "s"; // add "s" to make plural
            toolStripSplitButton1.Tag = DefaultSizeUnit;
            int x = 0;
            foreach (PropertyInfo prop in typeof(SizeUnit).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                toolStripSplitButton1.DropDownItems.Add(prop.Name, null, new EventHandler(ConvertFileSizeDropDownClick));
                PropertyInfo pinfo = typeof(SizeUnit).GetProperty(prop.Name);
                toolStripSplitButton1.DropDownItems[x].Tag = pinfo.GetValue("SizeUnit");
                x = ++x;
            }

            FlushMemory();
        }

        public void FlushMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
        }

        // Prevent flicker with the listview
        // Requires "using System.Reflection;"
        private static void SetDoubleBuffered(Control control) //Changed from public to private
        {
            // From comment at http://msdn.microsoft.com/en-us/library/system.windows.forms.listview.doublebuffered.aspx
            // Sets protected property Control.Double­Buffered to true. This is useful if you want to avoid flickering of controls 
            // such as ListView (when updating) or Panel (when you draw on it). All controls have property DoubleBuffered,
            // but this property is protected. Usually you need to create a new class (inherited from the control) and set the
            // protected property. Below a little hack. You can use reflection to access non-public methods and properties. 

            // set instance non-public property with name "DoubleBuffered" to true
            typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, control, new object[] { true });
        }

        private void UpdateSIDTooltip()
        {
            try
            {
                if (textBoxOwner.Text.Substring(0, 4) == "S-1-") //If entered text is already in SID format
                {
                    var OwnerSID = new SecurityIdentifier(textBoxOwner.Text);
                    toolTipOwner.SetToolTip(this.textBoxOwner, ""); //Clear Tooltip on Owner textbox
                }
                else
                {
                    // Get Owner's SID from Owner textbox
                    var objOwner = new NTAccount(textBoxOwner.Text);
                    var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); //if Domain or User cannot be found this line gets error
                    // MessageBox.Show(OwnerSID.ToString()); //debug line
                    toolTipOwner.SetToolTip(this.textBoxOwner, "SID: " + OwnerSID.ToString());
                }
            }
            catch
            {
                toolTipOwner.SetToolTip(this.textBoxOwner, ""); //Clear Tooltip on Owner textbox
            }
        }

        // Need to do this because UPN is only good for domain user accounts with this .NET 2.0 thinger (see the Convert to UPN method for more info)
        private void EnableDisableUPNTranslation()
        {
            string strSAMDomainUserName;
            string strDomainName = null; // You have to assign it some value outside the try because it's being used in the 'catch' section
            string strUserName;
            // We first need to convert Owner to Security Accounts Manager (SAM) Account Name
            try
            {
                if (textBoxOwner.Text.Substring(0, 4) == "S-1-") // If entered text is already in SID format
                {
                    var objOwner = new SecurityIdentifier(textBoxOwner.Text);
                    var OwnerSAM = objOwner.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSAM.ToString()); // debug line
                    strSAMDomainUserName = OwnerSAM.ToString();
                }
                else
                {
                    var objOwner = new NTAccount(textBoxOwner.Text);
                    var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); ; //Translate to SID 1st or else it won't work!
                    var OwnerSAM = OwnerSID.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSID.ToString()); // debug line
                    strSAMDomainUserName = OwnerSAM.ToString();
                }
            }
            catch
            {
                linkLabel3.Enabled = false;
                return;
            }

            // Pull just the username from Domain\Username
            try
            {
                string[] arrResult = strSAMDomainUserName.Split('\\');
                strDomainName = arrResult[0];
                strUserName = arrResult[1];
            }
            catch // In case there is no domain name
            {
                string[] arrResult = strSAMDomainUserName.Split('\\'); 
                strUserName = arrResult[0];
            }
            
            DirectoryEntry dirEntryLocalMachine =
            new DirectoryEntry("WinNT://" + Environment.MachineName + ",computer");

            try // If account exists locally, disable the UPN link
            {
                if (dirEntryLocalMachine.Children.Find(strUserName) != null)
                {
                    linkLabel3.Enabled = false;
                }
            }
            catch
            {
                if (strDomainName == "BUILTIN" || strDomainName == "NT AUTHORITY" || strDomainName == "CREATOR GROUP" || strDomainName == "CREATOR OWNER" || strDomainName == "Everyone")
                {
                    linkLabel3.Enabled = false;
                    return;
                }
                else
                {
                    linkLabel3.Enabled = true;
                    return;
                }
            }
        }

        // Folder path browser
        private void button1_Click(object sender, EventArgs e)
        {           
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                textBoxSearchPath.Text = folderBrowserDialog1.SelectedPath;
            }
        }

        // Search button clicked
        private void button2_Click(object sender, EventArgs e)
        {
            if (button2.Text == "Stop" || button2.Text == "Cancelling") // Button should be disabled while cancelling, but checks for that just in case
            {
                backgroundWorkerSearch.CancelAsync(); // Note cancelling the backgroundWorker still calls the RunWorkerCompleted function
                button2.Text = "Cancelling";
                button2.Enabled = false;
            }
            else
            {

                SearchErrorCount = 0;
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****BEGIN SEARCH****";

                if  (Directory.Exists(textBoxSearchPath.Text))
                {
                    try
                    {
                        if (textBoxOwner.Text.Substring(0, 4) == "S-1-") //If entered text is already in SID format
                        {
                            var OwnerSID = new SecurityIdentifier(textBoxOwner.Text);
                            backgroundWorkerSearch.RunWorkerAsync(OwnerSID);
                        }
                        else
                        {
                            // Get Owner's SID from Owner textbox
                            var objOwner = new NTAccount(textBoxOwner.Text);
                            // var objOwner = new NTAccount(@"Seker"); //debug line
                            var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); //if Domain or User cannot be found this line gets error
                            // MessageBox.Show(OwnerSID.ToString()); //debug line
                            backgroundWorkerSearch.RunWorkerAsync(OwnerSID);
                        }
                    }
                    catch
                    {
                        MessageBox.Show("Please check your owner and try again.\r\n\r\n-You may search by a local or domain user or group\r\n" +
                            "-Domain qualifier is often required when searching Active Directory\r\n" +
                            "-You may also search using SID values\r\n" +
                            "-When searching by a domain object, make sure you can contact a domain controller", "Cannot Resolve User/Group Account");
                        return;
                    }
                    // If Folder Path is valid, go ahead and work    
                    // Change Search button to STOP
                    button2.Text = "Stop";
                    // Disable other text boxes and buttons & the listview
                    textBoxOwner.Enabled = false;
                    textBoxSearchPath.Enabled = false;
                    button1.Enabled = false;
                    button3.Enabled = false;
                    listView1.Enabled = false;
                    // Clear the listview & disable the export list button
                    listView1.Items.Clear();
                    toolStripButton3.Enabled = false;
                    // Reset the status bar label tags & labels
                    toolStripStatusLabel1.Tag = 0;
                    toolStripStatusLabel2.Tag = 0;
                    toolStripStatusLabel3.Tag = "";
                    toolStripStatusLabel1.Text = "Folders: 0"; // Folders found
                    toolStripStatusLabel2.Text = "Files: 0"; // Files found
                    toolStripStatusLabel3.Text = textBoxSearchPath.Text; // Current search directory
                    //Animate progress bar
                    progressBar1.Style = ProgressBarStyle.Marquee;
                }
                else MessageBox.Show("Please check your search path and try again.", "Invalid Path");
            }
        }

        private void backgroundWorkerSeach_DoWork(object sender, DoWorkEventArgs e)
        {
            IdentityReference OwnerSIDForBW = (IdentityReference)e.Argument;

            // Perform search and add to the listview. Need to "try" otherwise there is an error if there is access denied to the path
            try
            { 
                foreach (string f in Directory.GetFileSystemEntries(textBoxSearchPath.Text))
                {
                    AddListItems(f, OwnerSIDForBW);
                }
            }
            catch
            {
                SearchErrorCount = ++SearchErrorCount;
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] Access Denied > " + textBoxSearchPath.Text;
            }
        }

        private void AddListItems(string input, IdentityReference input2)
        {
            if (backgroundWorkerSearch.CancellationPending) return; // Triggers WorkCompleted if STOP button pressed

            try
            {
                // See if it's a file               
                if (File.Exists(input))
                {
                    if (File.GetAccessControl(input).GetOwner(typeof(SecurityIdentifier)) == input2) // Compare SID
                    {
                        string filesize = (new FileInfo(input)).Length.ToString(); // Length property gets the size, in bytes, of the current file and returns it as an Int64. Turns that into a string for the listviewsubitem
                        SizeUnit SelectedSizeUnit = (SizeUnit)toolStripSplitButton1.Tag;
                        ConvertFileSize ConvertFileSize = new ConvertFileSize();
                        string filesizeformatted = ConvertFileSize.ConvertSize(Convert.ToDouble(filesize), SizeUnit.B, SelectedSizeUnit).ToString(); // Always from bytes
                        filesizeformatted = FormatFileSize(filesizeformatted, SelectedSizeUnit);
                        ListViewItem listitem = new ListViewItem(input); // Create list item with Name (full path)
                        listitem.SubItems.Add((Path.GetFileName(input)).Trim()); // Add Filename
                        listitem.SubItems.Add(filesizeformatted); // Add Size in a readable format
                        listitem.SubItems.Add(filesize); // Add actual size as a hidden subitem
                        toolStripStatusLabel2.Tag = Convert.ToInt64(toolStripStatusLabel2.Tag) + 1;
                        listView1List.Add(listitem); // Add item to Array
                        backgroundWorkerSearch.ReportProgress(0); // Update lables on Form
                    }
                }
                else // If it's not a file, see if it's a folder or missing (maybe it was deleted after the search completed)
                {
                    if (Directory.Exists(input))
                    {
                        if (Directory.GetAccessControl(input).GetOwner(typeof(SecurityIdentifier)) == input2) // Compare SID
                        {
                            ListViewItem listitem = new ListViewItem(input);
                            listitem.SubItems.Add(Path.GetFileName(input)); // Add Folder Name
                            listitem.SubItems.Add("(folder)"); // Add Size
                            toolStripStatusLabel1.Tag = Convert.ToInt64(toolStripStatusLabel1.Tag) + 1;
                            listView1List.Add(listitem); //Add item to Array
                        }
                        
                        // These two lines are outside the "Directory Exists so we can get an up-to-date folder thinger.
                        // There's no point in putting it outside the File compare SID IF section since the folder wouldn't have updated nor any numbers
                        toolStripStatusLabel3.Tag = input;
                        backgroundWorkerSearch.ReportProgress(0); //Update lables on Form
                       
                        foreach (string f in Directory.GetFileSystemEntries(input))
                        {
                            AddListItems(f, input2);
                        }
                    }
                }
            }
            catch (Exception)
            {
                //Just skip on folder errors such as "access is denied" or "path not found"
                //Need to make note about this later.

                SearchErrorCount = ++SearchErrorCount;
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] Access Denied > " + input;
            }
        }

        private void backgroundWorkerSeach_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // If I decided to pass a value, such as a ListViewItem
            // ListViewItem listitem = (ListViewItem)e.UserState;
            
            // Update status labels every 1/10 second to keep allow the GUI to catch up a little (such as the progress bar)
            if ((DateTime.UtcNow - lastRefreshGuiLabelsTime).TotalMilliseconds > Convert.ToInt64(100))
            {
                toolStripStatusLabel1.Text = "Folders: " + toolStripStatusLabel1.Tag;
                toolStripStatusLabel2.Text = "Files: " + toolStripStatusLabel2.Tag;
                toolStripStatusLabel3.Text = toolStripStatusLabel3.Tag.ToString();
                lastRefreshGuiLabelsTime = DateTime.UtcNow;
            }
        }

        private void backgroundWorkerSeach_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Update labels
            toolStripStatusLabel1.Text = "Folders: " + toolStripStatusLabel1.Tag;
            toolStripStatusLabel2.Text = "Files: " + toolStripStatusLabel2.Tag;
            toolStripStatusLabel3.Text = "Sorting...";
            Application.DoEvents(); // To make sure the "Sorting..." text appears (though it's actually loading into the control, then sorting)
            
            // If list is not empty add items to the listview
            if (listView1List.Count != 0)
            {
                listView1.Items.AddRange(listView1List.ToArray());
                listView1List.Clear();
            }

            ResizeColumnWidths();

            toolStripButton3.Enabled = true;
            // Finish Up
            if (button2.Text != "Cancelling")
            {
                toolStripStatusLabel3.Text = "Done!";
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****END SEARCH (FOUND FOLDERS: " + toolStripStatusLabel1.Tag + " & FOUND FILES: " + toolStripStatusLabel2.Tag + ")****" + Environment.NewLine;
            }
            else
            {
                toolStripStatusLabel3.Text = "Search Canceled";
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****SEARCH CANCELED (FOUND FOLDERS: " + toolStripStatusLabel1.Tag + " & FOUND FILES " + toolStripStatusLabel2.Tag + ")****" + Environment.NewLine;
            }
            // Stop progress bar animation
            progressBar1.Style = ProgressBarStyle.Blocks;
            // Change Stop button to SEARCH and enable (in case STOP button was pressed)
            button2.Text = "Search";
            button2.Enabled = true;
            // Enable other text boxes and buttons
            textBoxOwner.Enabled = true;
            textBoxSearchPath.Enabled = true;
            button1.Enabled = true;
            button3.Enabled = true;
            listView1.Enabled = true;

            // If log form is open update the view
            if (fLog != null && fLog.bLogFormIsOpen == true)
            {
                fLog.strTextToShow = strlogging;
                fLog.ScrollErrorLogToBottom();
            }

            if (SearchErrorCount > 0) { MessageBox.Show("There were errors accessing some folders or files. You must have permissions to these locations. You can also run this program with Admin privileges if you would like to do so. Click the red information button for a log of locations with errors.", "Sorry!"); }

            FlushMemory();
        }

        private void ResizeColumnWidths()
        {
            // Resize header columns if listview is not empty and enable file export of the list
            if (listView1.Items.Count != 0) // Don't resize if nothing is in the list
            {
                listView1.BeginUpdate(); // Prevents multiple resizing from showing (I resize a couple times due to the suckiness of the built-in options)

                // Autosize by contents
                listView1.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent); // resizes columns based on content, but could cause horizontal scrolling
                // Alternative way to Autosize by Contents & header I was testing but it makes the last column stretch to fill the window which is not what we want (-1 is by data & -2 is by both data and header)
                // foreach (ColumnHeader col in listView1.Columns)
                // {
                //    col.Width = -2;
                // }

                // Preferred Column Widths - Increase size if necessary
                if (columnHeaderPath.Width < 200) columnHeaderPath.Width = 200; // Don't let the Folder/File path column get too small (or it cuts off the header)     
                if (columnHeaderFilename.Width < 100) columnHeaderFilename.Width = 100; // Don't let the Filename column get too small (or it cuts off the header)  
                if (columnHeaderSize.Width < 100) columnHeaderSize.Width = 100; // Don't let the Size column get too small (or it cuts off the header - "Size (megabytes)" seems to be the longest) 

                // Shrink Path Column Width if necessary (but don't go less than 200)
                if ((columnHeaderPath.Width + columnHeaderFilename.Width + columnHeaderSize.Width) > (listView1.ClientSize.Width)) // If horizontal scrolling, shrink the File/Folder column to fit
                    columnHeaderPath.Width = Math.Max(listView1.ClientSize.Width - (columnHeaderFilename.Width + columnHeaderSize.Width), 200);

                // Now Shrink Filename Column Width if necessary (but don't go less than 100)
                if ((columnHeaderPath.Width + columnHeaderFilename.Width + columnHeaderSize.Width) > (listView1.ClientSize.Width)) // If horizontal scrolling, shrink the File/Folder column to fit
                    columnHeaderFilename.Width = Math.Max(listView1.ClientSize.Width - (columnHeaderPath.Width + columnHeaderSize.Width), 100);

                // Now Shrink Size Column Width if necessary (but don't go less than 100)
                if ((columnHeaderPath.Width + columnHeaderFilename.Width + columnHeaderSize.Width) > (listView1.ClientSize.Width)) // If horizontal scrolling, shrink the File/Folder column to fit
                    columnHeaderSize.Width = Math.Max(listView1.ClientSize.Width - (columnHeaderPath.Width + columnHeaderFilename.Width), 100);

                listView1.EndUpdate(); // Allows the final column resizing to show 
            }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start("http://www.grimadmin.com/profiles.php?uid=2");
            }
            catch { }
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start("http://www.grimadmin.com/staticpages/index.php/file-owner-versioncheck?version=" + Application.ProductVersion.ToString());
            }
            catch { }
        }

        // Convert Owner to UPN
        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Hard to get the User Principal Name (UPN) in .NET 2.0. (Easy in .NET 3.5 and up using the System.DirectoryServices.AccountManagement namespace -> http://stackoverflow.com/questions/2503011/given-a-users-sid-how-do-i-get-their-userprincipalname)

            // So, for now we get the SAM Account Name and guess the UPN has the same values.
            // Note that this is only for Domain accounts. User accounts should not be able to access this method since it should be grayed out.
            try
            {
                if (textBoxOwner.Text.Substring(0, 4) == "S-1-") // If entered text is already in SID format
                {
                    var objOwner = new SecurityIdentifier(textBoxOwner.Text);
                    var OwnerSAM = objOwner.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSAM.ToString()); // debug line   
                    textBoxOwner.Text = GetUpnForDomainName(OwnerSAM.ToString());
                    UpdateSIDTooltip();
                }
                else
                {
                    var objOwner = new NTAccount(textBoxOwner.Text);
                    var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); ; // Translate to SID 1st or else it won't work!
                    var OwnerSAM = OwnerSID.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSID.ToString()); // debug line

                    

                    string[] arrResult = OwnerSAM.Value.Split('\\');
                    string strOwnerUPN = arrResult[1] + "@" + arrResult[0];
                    //MessageBox.Show(strOwnerUPN); // debug line
                    textBoxOwner.Text = GetUpnForDomainName(OwnerSAM.ToString());
                    UpdateSIDTooltip();
                }
            }
            catch
            {
                MessageBox.Show("Please check your owner and try again.\r\n\r\n-Account must be a domain user or group to convert to its UPN\r\n" +
                    "-Domain qualifier is often required when searching Active Directory\r\n" +
                    "-Converting to UPN requires that you can contact a domain controller", "Cannot Resolve User/Group Account");
                return;
            }  
        }

        // Converts given value to UPN from SAM value
        string GetUpnForDomainName(string domainUserName)
        {

            string directoryServerName = null;
            string rootDomainDc = null;
            string samAccountName = null;

            string[] ntlmNameParts = domainUserName.Split(("\\").ToCharArray());

            samAccountName = ntlmNameParts[1];
            using (DirectoryEntry rootDe = new DirectoryEntry("LDAP://RootDSE"))
            {
                directoryServerName = rootDe.Properties["dnsHostName"].Value.ToString();
                rootDomainDc = rootDe.Properties["rootDomainNamingContext"].Value.ToString();
            }

            string ldapDomainEntryPath = @"LDAP://" + directoryServerName + @"/" + rootDomainDc;

            using (DirectoryEntry domainEntry = new DirectoryEntry(ldapDomainEntryPath))
            {
                using (DirectorySearcher searcher = new DirectorySearcher(domainEntry))
                {
                    searcher.SearchScope = SearchScope.Subtree;
                    searcher.PropertiesToLoad.Add("userPrincipalName");
                    searcher.Filter = "(&(objectClass=user)(samAccountName=" + samAccountName + "))";
                    SearchResult userResult = searcher.FindOne();
                    if (userResult == null)
                        throw new ArgumentException("User {0} not found.", domainUserName);
                    return userResult.Properties["userPrincipalName"][0].ToString();
                }
            }
        }

        // Convert Owner to SID
        private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            
            try
            {
                if (textBoxOwner.Text.Substring(0, 4) == "S-1-") // If entered text is already in SID format
                {
                    // Do nothing
                }
                else
                {
                    // Get Owner's SID from username
                    var objOwner = new NTAccount(textBoxOwner.Text);
                    // var objOwner = new NTAccount(@"Seker"); // debug line
                    var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); // If Domain or User cannot be found this line gets error
                    //MessageBox.Show(OwnerSID.ToString()); // debug line
                    toolTipOwner.SetToolTip(this.textBoxOwner, textBoxOwner.Text); // Set tooltip to username typed in
                    textBoxOwner.Text = OwnerSID.ToString();
                }
            }
            catch
            {
                MessageBox.Show("Please check your owner and try again.\r\n\r\n-You may search by a local or domain user or group\r\n" +
                    "-Domain qualifier is often required when searching Active Directory\r\n" +
                    "-When searching by a domain object, make sure you can contact a domain controller", "Cannot Resolve User/Group Account");
                return;
            }  
        }

        // Convert Owner to Security Accounts Manager (SAM) Account Name
        private void linkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {  
            try
            {
                if (textBoxOwner.Text.Substring(0, 4) == "S-1-") // If entered text is already in SID format
                {
                    var objOwner = new SecurityIdentifier(textBoxOwner.Text);
                    var OwnerSAM = objOwner.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSAM.ToString()); // debug line
                    textBoxOwner.Text = OwnerSAM.ToString();
                    UpdateSIDTooltip();
                }
                else
                {
                    var objOwner = new NTAccount(textBoxOwner.Text);
                    var OwnerSID = objOwner.Translate(typeof(SecurityIdentifier)); ; //Translate to SID 1st or else it won't work!
                    var OwnerSAM = OwnerSID.Translate(typeof(NTAccount));
                    //MessageBox.Show(OwnerSID.ToString()); // debug line
                    textBoxOwner.Text = OwnerSAM.ToString();
                    UpdateSIDTooltip();
                }
            }
            catch
            {
                MessageBox.Show("Please check your owner and try again.\r\n\r\n-You may search by a local or domain user or group\r\n" +
                    "-Domain qualifier is often required when searching Active Directory\r\n" +
                    "-When searching by a domain object, make sure you can contact a domain controller", "Cannot Resolve User/Group Account");
                return;
            }  
        }

        private void linkLabel1_MouseEnter(object sender, EventArgs e)
        {
            linkLabel1.LinkColor = System.Drawing.Color.DarkOrange;
            linkLabel1.VisitedLinkColor = System.Drawing.Color.DarkOrange;
        }

        private void linkLabel2_MouseEnter(object sender, EventArgs e)
        {
            linkLabel2.LinkColor = System.Drawing.Color.DarkOrange;
            linkLabel2.VisitedLinkColor = System.Drawing.Color.DarkOrange;
        }

        private void linkLabel3_MouseEnter(object sender, EventArgs e)
        {
            linkLabel3.LinkColor = System.Drawing.Color.DarkOrange;
            linkLabel3.VisitedLinkColor = System.Drawing.Color.DarkOrange;
        }

        private void linkLabel4_MouseEnter(object sender, EventArgs e)
        {
            linkLabel4.LinkColor = System.Drawing.Color.DarkOrange;
            linkLabel4.VisitedLinkColor = System.Drawing.Color.DarkOrange;
        }

        private void linkLabel5_MouseEnter(object sender, EventArgs e)
        {
            linkLabel5.LinkColor = System.Drawing.Color.DarkOrange;
            linkLabel5.VisitedLinkColor = System.Drawing.Color.DarkOrange;
        }

        private void linkLabel1_MouseLeave(object sender, EventArgs e)
        {
            linkLabel1.LinkColor = System.Drawing.Color.RoyalBlue;
            linkLabel1.VisitedLinkColor = System.Drawing.Color.RoyalBlue;
        }

        private void linkLabel2_MouseLeave(object sender, EventArgs e)
        {
            linkLabel2.LinkColor = System.Drawing.Color.RoyalBlue;
            linkLabel2.VisitedLinkColor = System.Drawing.Color.RoyalBlue;
        }

        private void linkLabel3_MouseLeave(object sender, EventArgs e)
        {
            linkLabel3.LinkColor = System.Drawing.Color.RoyalBlue;
            linkLabel3.VisitedLinkColor = System.Drawing.Color.RoyalBlue;
        }

        private void linkLabel4_MouseLeave(object sender, EventArgs e)
        {
            linkLabel4.LinkColor = System.Drawing.Color.RoyalBlue;
            linkLabel4.VisitedLinkColor = System.Drawing.Color.RoyalBlue;
        }

        private void linkLabel5_MouseLeave(object sender, EventArgs e)
        {
            linkLabel5.LinkColor = System.Drawing.Color.RoyalBlue;
            linkLabel5.VisitedLinkColor = System.Drawing.Color.RoyalBlue;
        }

        private void listView1_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            // Determine if clicked column is already the column that is being sorted.
            // But first change it so that if it's the display column it sorts by the hidden collumn

            if (e.Column == lvwColumnSorter.SortColumn)
            {
                // Reverse the current sort direction for this column.
                if (lvwColumnSorter.Order == SortOrder.Ascending)
                {
                    lvwColumnSorter.Order = SortOrder.Descending;
                }
                else
                {
                    lvwColumnSorter.Order = SortOrder.Ascending;
                }
            }
            else
            {
                // Set the column number that is to be sorted; default to ascending.
                lvwColumnSorter.SortColumn = e.Column;
                lvwColumnSorter.Order = SortOrder.Ascending;
            }

            // Perform the sort with these new sort options.
            this.listView1.Sort();

            FlushMemory();
        }

        // Shell Context Menu Right-Click
        private void listView1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (listView1.SelectedItems.Count != 0)
                {
                    //See if it's a file                 
                    if (File.Exists(listView1.SelectedItems[0].Text))
                    {
                        ShellContextMenu ctxMnu = new ShellContextMenu();
                        FileInfo[] arrFI = new FileInfo[1];
                        arrFI[0] = new FileInfo(listView1.SelectedItems[0].Text); //only does the 1st item selected so I turned MultiSelect on the listview off
                        ctxMnu.ShowContextMenu(arrFI, listView1.PointToScreen(new Point(e.X, e.Y)));
                    }
                    else //If it's not a file, see if it's a folder or missing (maybe it was deleted after the search completed)
                    {
                        if (Directory.Exists(listView1.SelectedItems[0].Text))
                        {
                            ShellContextMenu ctxMnu = new ShellContextMenu();
                            DirectoryInfo[] dir = new DirectoryInfo[1];
                            dir[0] = new DirectoryInfo(listView1.SelectedItems[0].Text);
                            ctxMnu.ShowContextMenu(dir, listView1.PointToScreen(new Point(e.X, e.Y)));
                        }
                    }
                }
                FlushMemory();
            }
        }

        // Browse for domain users or groups
        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
                ObjectTypes allowedTypes = ObjectTypes.None;
                allowedTypes = CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Users | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Groups | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.BuiltInGroups | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.WellKnownPrincipals; //Removed CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Contacts | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Computers | 
            
                ObjectTypes defaultTypes = ObjectTypes.None;
                defaultTypes = CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Users | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Groups;
                
                Locations allowedLocations = Locations.None;
                allowedLocations = CubicOrange.Windows.Forms.ActiveDirectory.Locations.LocalComputer | CubicOrange.Windows.Forms.ActiveDirectory.Locations.JoinedDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.EnterpriseDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.GlobalCatalog | CubicOrange.Windows.Forms.ActiveDirectory.Locations.ExternalDomain; // | CubicOrange.Windows.Forms.ActiveDirectory.Locations.Workgroup | CubicOrange.Windows.Forms.ActiveDirectory.Locations.UserEntered;
                
                Locations defaultLocations = Locations.None;
                defaultLocations = CubicOrange.Windows.Forms.ActiveDirectory.Locations.LocalComputer | CubicOrange.Windows.Forms.ActiveDirectory.Locations.JoinedDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.EnterpriseDomain;
                
                // Show dialog
                DirectoryObjectPickerDialog picker = new DirectoryObjectPickerDialog();
                picker.AllowedObjectTypes = allowedTypes;
                picker.DefaultObjectTypes = defaultTypes;
                picker.AllowedLocations = allowedLocations;
                picker.DefaultLocations = defaultLocations;
                picker.MultiSelect = false;
                picker.TargetComputer = "";
                DialogResult dialogResult = picker.ShowDialog(this);
                if (dialogResult == DialogResult.OK)
                {
                    DirectoryObject[] results;
                    results = picker.SelectedObjects;
                    if (results == null)
                    {
                        MessageBox.Show("Error: Results null.");
                        return;
                    }

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();

                    for (int i = 0; i <= results.Length - 1; i++)
                    {
                        sb.Append(string.Format("Name: \t\t {0}", results[i].Name));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("UPN: \t\t {0}", results[i].Upn));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("Path: \t\t {0}", results[i].Path));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("Schema Class: \t\t {0} ", results[i].SchemaClassName));
                        sb.Append(Environment.NewLine);
                        sb.Append(Environment.NewLine);

                        //If on domain, UPN will most likely appear, otherwise it will probably be blank
                        if (results[i].Upn == "")
                        {
                            textBoxOwner.Text = results[i].Name;
                        }
                        else
                        {
                            textBoxOwner.Text = results[i].Upn;
                        }
                    }
                    //MessageBox.Show(sb.ToString()); //debug line showing results

                    //Update ToolTip with SID
                    UpdateSIDTooltip();

                    //Enable Disable UPN conversion link
                    EnableDisableUPNTranslation();
                }
                else
                {
                    //MessageBox.Show("Dialog result: " + dialogResult.ToString()); //debug line when canceled is clicked
                }
            }
            catch (Exception e1)
            {
                MessageBox.Show(e1.ToString()); //Error catch
            }
            FlushMemory();
        }

        // Only allow the file/folder modification buttons if a list item is selected
        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count != 0)
            {
                toolStripButton1.Enabled = true; // "Open Containing Folder" button
                toolStripStatusLabel4.Enabled = true; // "Set Owner To:" label
                toolStripDropDownButton1.Enabled = true; // "Set Owner To:" dropdown button
                toolStripButton2.Enabled = true; // Go button
            }
            else
            {
                toolStripButton1.Enabled = false; // "Open Containing Folder" button
                toolStripStatusLabel4.Enabled = false; // "Set Owner To:" label
                toolStripDropDownButton1.Enabled = false; // "Set Owner To:" dropdown button
                if ((string)toolStripButton2.Tag != "Running" && (string)toolStripButton2.Tag != "Canceled")
                { 
                    toolStripButton2.Enabled = false; // Go button
                }
            }
        }

        // Disable the file/folder modification buttons if the listview loses focus
        private void listView1_Leave(object sender, EventArgs e)
        {
            toolStripButton1.Enabled = false; // "Open Containing Folder" button
            toolStripStatusLabel4.Enabled = false; // "Set Owner To:" label
            toolStripDropDownButton1.Enabled = false; // "Set Owner To:" dropdown button
            toolStripButton2.Enabled = false; // Go button
        }

        // "Open Containing Folder" button was clicked
        private void toolStripButton1_ButtonClick(object sender, EventArgs e)
        {
            Process p = new Process();

            // Notify User That Only the folder of the 1st selected item will open if more than one is selected.
            if (listView1.SelectedItems.Count > 1) MessageBox.Show("Only the containing folder for the 1st item selected will open.", "Note");

            string PathToFolderOrFile = listView1.SelectedItems[0].Text;
            if (!File.Exists(PathToFolderOrFile)) // It's not a file!
            {
                if (!Directory.Exists(PathToFolderOrFile)) // It's not a folder either!
                {
                    return; // Do nothing for now
                }
            }
            // Combine the arguments together
            // The quotes are added around the path in case it contains a comma or something else weird
            string argument = @"/select, " + "\"" + PathToFolderOrFile + "\""; // It doesn't matter if there's a space after 'select,'
            Process.Start("explorer.exe", argument);
            FlushMemory();
        }

        // The "Set Owner To" dropdown changed to "Current User"
        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            toolStripDropDownButton1.Text = "Current User";
        }

        // The "Set Owner To" dropdown changed to "Administrators" group
        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            toolStripDropDownButton1.Text = "Administrators";
        }


        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            toolStripDropDownButton1.Text = "SYSTEM";
        }

        // Set Owner to GO button
        private void toolStripButton2_ButtonClick(object sender, EventArgs e)
        {
            if ((string)toolStripButton2.Tag == "Running") // Button should be disabled while cancelling, but checks for that just in case
            {
                toolStripButton2.Enabled = false;
                toolStripButton2.Tag = "Canceled"; 
                backgroundWorkerSetOwner.CancelAsync(); // Note cancelling the backgroundWorker still calls the RunWorkerCompleted function
            }
            else
            {
                button2.Enabled = false;
                toolStripButton2.Tag = "Running";
                toolStripButton2.Image = WindowsFormsApplication1.Properties.Resources.Cancel;
                toolStripStatusLabel2.Tag = 0;
                toolStripStatusLabel1.Text = "Selected: " + listView1.SelectedItems.Count;
                toolStripStatusLabel2.Text = "Successful: 0"; // Files found
                toolStripStatusLabel3.Text = listView1.SelectedItems[0].Text;
                // Animate progress bar
                progressBar1.Style = ProgressBarStyle.Marquee;

                // Set Owner
                backgroundWorkerSetOwner.RunWorkerAsync();
            }
        }

        private void backgroundWorkerSetOwner_DoWork(object sender, DoWorkEventArgs e)
        {
            SetOwnerErrorCount = 0;

            // Allow this process to circumvent ACL restrictions
            WinAPI.ModifyPrivilege(PrivilegeName.SeRestorePrivilege, true);
            // Sometimes this is required and other times it works without it. Not sure when.
            WinAPI.ModifyPrivilege(PrivilegeName.SeTakeOwnershipPrivilege, true);

            strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****BEGIN SET OWNER****";


            GetPathList _getFilePathList = new GetPathList(GetPaths);
            List<string> pathList = (List<string>)this.listView1.Invoke(_getFilePathList);

            foreach (string FileToSetOwner in pathList)
            {
                if (backgroundWorkerSetOwner.CancellationPending) return; // Triggers WorkCompleted if STOP icon button pressed

                String strFilePathToChangeOwner = FileToSetOwner;
                var fs = System.IO.File.GetAccessControl(strFilePathToChangeOwner);

                // Set owner property in the FS
                switch (toolStripDropDownButton1.Text)
                {
                    case "SYSTEM":
                        fs.SetOwner(new NTAccount("NT AUTHORITY\\SYSTEM"));
                        break;
                    case "Current User":
                        fs.SetOwner(new NTAccount(Environment.UserDomainName, Environment.UserName));
                        break;
                    case "Administrators":
                        fs.SetOwner(new NTAccount("BUILTIN\\Administrators"));
                        break;
                    default:
                        fs.SetOwner(new NTAccount(toolStripDropDownButton1.Text));
                        break;
                }

                // Set the Owner
                string strOwnerAsText = fs.GetOwner(typeof(System.Security.Principal.NTAccount)).ToString();
                try
                {
                    System.IO.File.SetAccessControl(strFilePathToChangeOwner, fs);
                    strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] Owner set to \"" + strOwnerAsText + "\" > " + strFilePathToChangeOwner;
                    toolStripStatusLabel2.Tag = Convert.ToInt64(toolStripStatusLabel2.Tag) + 1; // Add 1 if successful
                }
                catch
                {
                    SetOwnerErrorCount = ++SetOwnerErrorCount;
                    strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] Cannot set owner to \"" + strOwnerAsText + "\" > " + strFilePathToChangeOwner;
                }

                // Update Form Tags and update form
                toolStripStatusLabel3.Tag = FileToSetOwner;
                backgroundWorkerSetOwner.ReportProgress(0); // Update lables on Form
            }
            
            // End logging and message if error
            strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****END SET OWNER****" + Environment.NewLine;
        }

        private void backgroundWorkerSetOwner_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // Update status labels every 1/10 second to keep allow the GUI to catch up a little (such as the progress bar)
            if ((DateTime.UtcNow - lastRefreshGuiLabelsTime).TotalMilliseconds > Convert.ToInt64(100))
            {
                toolStripStatusLabel2.Text = "Successful: " + toolStripStatusLabel2.Tag;
                toolStripStatusLabel3.Text = toolStripStatusLabel3.Tag.ToString();
                lastRefreshGuiLabelsTime = DateTime.UtcNow;
            }
        }

        private void backgroundWorkerSetOwner_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Update labels and Finish Up
            toolStripStatusLabel2.Text = "Successful: " + toolStripStatusLabel2.Tag;
            if ((string)toolStripButton2.Tag != "Canceled")
            {
                toolStripStatusLabel3.Text = "Done!";
            }
            else
            {
                toolStripStatusLabel3.Text = "Set Owner Canceled";
                strlogging = strlogging + Environment.NewLine + "[" + DateTime.Now.ToString("yyyy-dd-M HH:mm:ss (K)") + "] ****SET OWNER CANCELED****" + Environment.NewLine;
            }
            // Stop progress bar animation
            progressBar1.Style = ProgressBarStyle.Blocks;
            // Change Set Owner Button Back to an Arrow
            toolStripButton2.Tag = "Completed";
            toolStripButton2.Image = WindowsFormsApplication1.Properties.Resources.GoArrow;
            if (listView1.SelectedItems.Count != 0) { toolStripButton2.Enabled = true; }
            else { toolStripButton2.Enabled = false; }
            button2.Enabled = true;


            // If log form is open update the view
            if (fLog != null && fLog.bLogFormIsOpen == true)
            {
                fLog.strTextToShow = strlogging;
                fLog.ScrollErrorLogToBottom();
            }

            if (SetOwnerErrorCount > 0) { MessageBox.Show("You must have Administrator permissions to change the owner to anyone except yourself. Please run this program with Admin privileges if you would like to do so. If you are attempting to take ownership for yourself, make sure you have the \"Take ownership\" permission or have Admin privileges. Click the red information button for a log of locations with errors.", "Sorry!"); }

            FlushMemory();
        }

        //Export to file!!!!
        private void toolStripButton3_ButtonClick(object sender, EventArgs e)
        {
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                saveFileDialog1.ToString();

                StringBuilder listViewContent = new StringBuilder();

                listViewContent.Append("\"Path\",\"Name\",\"" + this.columnHeaderSize.Text + "\"");
                listViewContent.Append(Environment.NewLine);

                foreach (ListViewItem item in this.listView1.Items)
                {               
                    listViewContent.Append("\"");
                    listViewContent.Append(item.Text);
                    listViewContent.Append("\",\"");
                    listViewContent.Append(item.SubItems[1].Text);
                    listViewContent.Append("\",\"");
                    listViewContent.Append(item.SubItems[2].Text);
                    listViewContent.Append("\"");
                    listViewContent.Append(Environment.NewLine);
                }

                try
                {
                    TextWriter tw = new StreamWriter(saveFileDialog1.FileName);
                    tw.WriteLine(listViewContent.ToString());
                    tw.Close();
                }
                catch
                {
                    MessageBox.Show("Make sure you have access to write to the location and file.", "Error Writing File");
                }
            }
            FlushMemory();
        }

        private void textBoxOwner_Enter(object sender, EventArgs e)
        {
            toolTipOwner.SetToolTip(this.textBoxOwner, ""); // Clear Tooltip on Owner textbox
        }

        private void textBoxOwner_Leave(object sender, EventArgs e)
        {
            UpdateSIDTooltip(); // Update Tooltip

            // Enable Disable UPN conversion link
            if (textBoxOwner.Text != "")
            {
                EnableDisableUPNTranslation();
            }
        }

        private void buttonShowErrorLogForm_Click(object sender, EventArgs e)
        {
            // If form is not already open
            if (fLog == null || fLog.bLogFormIsOpen == false)
            {
                fLog = new FormErrorLog();
                fLog.strTextToShow = strlogging;
                fLog.Show();
                fLog.bLogFormIsOpen = true;
            }
            else // If form is already open
            {
                fLog.strTextToShow = strlogging;
                fLog.Activate();
                fLog.WindowState = FormWindowState.Normal;
            }           
        }

        private void listView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Control)
            {
                // This class is way faster than a foreach loop
                ListViewSelect.SelectAllItems(listView1);
            }
        }

        private void toolStripMenuItem6_Click(object sender, EventArgs e)
        {
            try
            {
                ObjectTypes allowedTypes = ObjectTypes.None;
                allowedTypes = CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Users | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Groups | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.BuiltInGroups | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.WellKnownPrincipals; //Removed CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Contacts | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Computers | 

                ObjectTypes defaultTypes = ObjectTypes.None;
                defaultTypes = CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Users | CubicOrange.Windows.Forms.ActiveDirectory.ObjectTypes.Groups;

                Locations allowedLocations = Locations.None;
                allowedLocations = CubicOrange.Windows.Forms.ActiveDirectory.Locations.LocalComputer | CubicOrange.Windows.Forms.ActiveDirectory.Locations.JoinedDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.EnterpriseDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.GlobalCatalog | CubicOrange.Windows.Forms.ActiveDirectory.Locations.ExternalDomain; // | CubicOrange.Windows.Forms.ActiveDirectory.Locations.Workgroup | CubicOrange.Windows.Forms.ActiveDirectory.Locations.UserEntered;

                Locations defaultLocations = Locations.None;
                defaultLocations = CubicOrange.Windows.Forms.ActiveDirectory.Locations.LocalComputer | CubicOrange.Windows.Forms.ActiveDirectory.Locations.JoinedDomain | CubicOrange.Windows.Forms.ActiveDirectory.Locations.EnterpriseDomain;

                // Show dialog
                DirectoryObjectPickerDialog picker = new DirectoryObjectPickerDialog();
                picker.AllowedObjectTypes = allowedTypes;
                picker.DefaultObjectTypes = defaultTypes;
                picker.AllowedLocations = allowedLocations;
                picker.DefaultLocations = defaultLocations;
                picker.MultiSelect = false;
                picker.TargetComputer = "";
                DialogResult dialogResult = picker.ShowDialog(this);
                if (dialogResult == DialogResult.OK)
                {
                    DirectoryObject[] results;
                    results = picker.SelectedObjects;
                    if (results == null)
                    {
                        MessageBox.Show("Error: Results null.");
                        return;
                    }

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();

                    for (int i = 0; i <= results.Length - 1; i++)
                    {
                        sb.Append(string.Format("Name: \t\t {0}", results[i].Name));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("UPN: \t\t {0}", results[i].Upn));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("Path: \t\t {0}", results[i].Path));
                        sb.Append(Environment.NewLine);
                        sb.Append(string.Format("Schema Class: \t\t {0} ", results[i].SchemaClassName));
                        sb.Append(Environment.NewLine);
                        sb.Append(Environment.NewLine);

                        //If on domain, UPN will most likely appear, otherwise it will probably be blank
                        if (results[i].Upn == "")
                        {
                            toolStripDropDownButton1.Text = results[i].Name;
                        }
                        else
                        {
                            toolStripDropDownButton1.Text = results[i].Upn;
                        }
                    }
                }
                else
                {
                    //MessageBox.Show("Dialog result: " + dialogResult.ToString()); //debug line when canceled is clicked
                }
            }
            catch (Exception e1)
            {
                MessageBox.Show(e1.ToString()); //Error catch
            }
            FlushMemory();
        }

        private void toolStripSplitButton1_ButtonClick(object sender, EventArgs e)
        {
            ConvertFileSizeView(DefaultSizeUnit);
        }

        private void ConvertFileSizeDropDownClick(object sender, EventArgs e)
        {
            SizeUnit SizeUnit = (SizeUnit)((ToolStripMenuItem)sender).Tag;
            ConvertFileSizeView(SizeUnit);
        }

        private void ConvertFileSizeView(SizeUnit NewSizeUnit)
        {
            SizeUnit CurrentSizeUnit = SizeUnit.B; // Hidden field is ALWAYS BYTES
            ConvertFileSize ConvertFileSize = new ConvertFileSize();
            foreach (ListViewItem item in this.listView1.Items)
            {
                if (item.SubItems[2].Text == "(folder)")
                {
                    // Do Nothing
                }
                else
                {
                    // First get rid of commas.
                    string itemsize = item.SubItems[3].Text; // Pulling the hidden value that's not formatte or rounded
                    // itemsize = itemsize.Replace(",", ""); // OLD from before. Don't need to pull out commas anymore since we are now using the hidden size value

                    // Convert item size no new format
                    itemsize = (ConvertFileSize.ConvertSize(Convert.ToDouble(itemsize), CurrentSizeUnit, NewSizeUnit)).ToString();
                    item.SubItems[2].Text = FormatFileSize(itemsize, NewSizeUnit);
                }
            }
            toolStripSplitButton1.Text = NewSizeUnit.Name + "s"; // Add "s" to make plural
            toolStripSplitButton1.Tag = NewSizeUnit;
            this.columnHeaderSize.Text = "Size (" + NewSizeUnit.Name + "s)"; // Add "s" to make plural
            ResizeColumnWidths();
        }

        // Add commas and stuff
        private string FormatFileSize(string itemsize, SizeUnit sizeunit)
        {

            if (sizeunit == SizeUnit.B)
            {
                // This code adds commas to the file size if bytes
                for (int p = 3; p < itemsize.Length; p += 4)
                {
                    itemsize = itemsize.Insert(itemsize.Length - p, ",");
                }
            }
            else
            {
                itemsize = String.Format("{0:n}", Convert.ToDouble(itemsize)); // GAH THERE HAS TO BE A WAY SO I DON'T HAVE TO CONVERT THIS TO A NUMBER FIRST...
            }
            return itemsize;
        }
    }
}



