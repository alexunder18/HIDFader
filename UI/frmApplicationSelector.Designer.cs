namespace HIDMate.UI
{
    partial class frmApplicationSelector
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmApplicationSelector));
            this.applicationIcons = new System.Windows.Forms.ImageList(this.components);
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabApplications = new System.Windows.Forms.TabPage();
            this.listViewApplications = new System.Windows.Forms.ListView();
            this.panelAppButtons = new System.Windows.Forms.Panel();
            this.btnConfigure = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.tabMouse = new System.Windows.Forms.TabPage();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.chkStartWithWindows = new System.Windows.Forms.CheckBox();
            this.volumeStep = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.tabControl.SuspendLayout();
            this.tabApplications.SuspendLayout();
            this.panelAppButtons.SuspendLayout();
            this.panelButtons.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.volumeStep)).BeginInit();
            this.SuspendLayout();
            // 
            // applicationIcons
            // 
            this.applicationIcons.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            this.applicationIcons.ImageSize = new System.Drawing.Size(16, 16);
            this.applicationIcons.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // tabControl
            // 
            this.tabControl.Controls.Add(this.tabApplications);
            this.tabControl.Controls.Add(this.tabMouse);
            this.tabControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl.Location = new System.Drawing.Point(5, 5);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(623, 500);
            this.tabControl.TabIndex = 0;
            // 
            // tabApplications
            // 
            this.tabApplications.Controls.Add(this.listViewApplications);
            this.tabApplications.Controls.Add(this.panelAppButtons);
            this.tabApplications.Location = new System.Drawing.Point(4, 22);
            this.tabApplications.Name = "tabApplications";
            this.tabApplications.Padding = new System.Windows.Forms.Padding(3);
            this.tabApplications.Size = new System.Drawing.Size(615, 474);
            this.tabApplications.TabIndex = 0;
            this.tabApplications.Text = "Application bindings";
            this.tabApplications.UseVisualStyleBackColor = true;
            // 
            // listViewApplications
            // 
            this.listViewApplications.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listViewApplications.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listViewApplications.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listViewApplications.FullRowSelect = true;
            this.listViewApplications.GridLines = true;
            this.listViewApplications.HideSelection = false;
            this.listViewApplications.Location = new System.Drawing.Point(3, 3);
            this.listViewApplications.MultiSelect = false;
            this.listViewApplications.Name = "listViewApplications";
            this.listViewApplications.Size = new System.Drawing.Size(609, 418);
            this.listViewApplications.SmallImageList = this.applicationIcons;
            this.listViewApplications.TabIndex = 0;
            this.listViewApplications.UseCompatibleStateImageBehavior = false;
            this.listViewApplications.View = System.Windows.Forms.View.Details;
            // 
            // panelAppButtons
            // 
            this.panelAppButtons.Controls.Add(this.btnConfigure);
            this.panelAppButtons.Controls.Add(this.btnRefresh);
            this.panelAppButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelAppButtons.Location = new System.Drawing.Point(3, 421);
            this.panelAppButtons.Name = "panelAppButtons";
            this.panelAppButtons.Padding = new System.Windows.Forms.Padding(5);
            this.panelAppButtons.Size = new System.Drawing.Size(609, 50);
            this.panelAppButtons.TabIndex = 1;
            // 
            // btnConfigure
            // 
            this.btnConfigure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConfigure.Location = new System.Drawing.Point(391, 8);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Size = new System.Drawing.Size(100, 35);
            this.btnConfigure.TabIndex = 0;
            this.btnConfigure.Text = "Configure";
            // 
            // btnRefresh
            // 
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.Location = new System.Drawing.Point(497, 7);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(100, 35);
            this.btnRefresh.TabIndex = 1;
            this.btnRefresh.Text = "Refresh";
            // 
            // tabMouse
            // 
            this.tabMouse.Location = new System.Drawing.Point(4, 22);
            this.tabMouse.Name = "tabMouse";
            this.tabMouse.Padding = new System.Windows.Forms.Padding(3);
            this.tabMouse.Size = new System.Drawing.Size(625, 484);
            this.tabMouse.TabIndex = 1;
            this.tabMouse.Text = "Mouse";
            this.tabMouse.UseVisualStyleBackColor = true;
            // 
            // panelButtons
            // 
            this.panelButtons.Controls.Add(this.chkStartWithWindows);
            this.panelButtons.Controls.Add(this.volumeStep);
            this.panelButtons.Controls.Add(this.label1);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.panelButtons.Location = new System.Drawing.Point(5, 505);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Padding = new System.Windows.Forms.Padding(5);
            this.panelButtons.Size = new System.Drawing.Size(623, 40);
            this.panelButtons.TabIndex = 1;
            // 
            // chkStartWithWindows
            // 
            this.chkStartWithWindows.AutoSize = true;
            this.chkStartWithWindows.Location = new System.Drawing.Point(170, 14);
            this.chkStartWithWindows.Name = "chkStartWithWindows";
            this.chkStartWithWindows.Size = new System.Drawing.Size(119, 17);
            this.chkStartWithWindows.TabIndex = 2;
            this.chkStartWithWindows.Text = "Start with Windows";
            this.chkStartWithWindows.UseVisualStyleBackColor = true;
            // 
            // volumeStep
            // 
            this.volumeStep.Location = new System.Drawing.Point(110, 12);
            this.volumeStep.Name = "volumeStep";
            this.volumeStep.ReadOnly = true;
            this.volumeStep.Size = new System.Drawing.Size(39, 21);
            this.volumeStep.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 14);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(92, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Volume sensitivity";
            // 
            // frmApplicationSelector
            // 
            this.ClientSize = new System.Drawing.Size(633, 550);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.panelButtons);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "frmApplicationSelector";
            this.Padding = new System.Windows.Forms.Padding(5);
            this.tabControl.ResumeLayout(false);
            this.tabApplications.ResumeLayout(false);
            this.panelAppButtons.ResumeLayout(false);
            this.panelButtons.ResumeLayout(false);
            this.panelButtons.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.volumeStep)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ImageList applicationIcons;
        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabApplications;
        private System.Windows.Forms.ListView listViewApplications;
        private System.Windows.Forms.Panel panelAppButtons;
        private System.Windows.Forms.Button btnConfigure;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.TabPage tabMouse;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown volumeStep;
        private System.Windows.Forms.CheckBox chkStartWithWindows;
    }
}
