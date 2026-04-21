namespace HIDFader.UI
{
    partial class frmApplicationSelector
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmApplicationSelector));
            this.applicationIcons = new System.Windows.Forms.ImageList(this.components);
            this.listViewApplications = new System.Windows.Forms.ListView();
            this.panelButtons = new System.Windows.Forms.Panel();
            this.volumeStep = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.btnConfigure = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
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
            // listViewApplications
            // 
            this.listViewApplications.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listViewApplications.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listViewApplications.FullRowSelect = true;
            this.listViewApplications.GridLines = true;
            this.listViewApplications.HideSelection = false;
            this.listViewApplications.Location = new System.Drawing.Point(0, 0);
            this.listViewApplications.MultiSelect = false;
            this.listViewApplications.Name = "listViewApplications";
            this.listViewApplications.Size = new System.Drawing.Size(633, 277);
            this.listViewApplications.SmallImageList = this.applicationIcons;
            this.listViewApplications.TabIndex = 0;
            this.listViewApplications.UseCompatibleStateImageBehavior = false;
            this.listViewApplications.View = System.Windows.Forms.View.Details;
            // 
            // panelButtons
            // 
            this.panelButtons.Controls.Add(this.volumeStep);
            this.panelButtons.Controls.Add(this.label1);
            this.panelButtons.Controls.Add(this.btnConfigure);
            this.panelButtons.Controls.Add(this.btnRefresh);
            this.panelButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelButtons.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.panelButtons.Location = new System.Drawing.Point(0, 277);
            this.panelButtons.Name = "panelButtons";
            this.panelButtons.Padding = new System.Windows.Forms.Padding(5);
            this.panelButtons.Size = new System.Drawing.Size(633, 50);
            this.panelButtons.TabIndex = 1;
            // 
            // volumeStep
            // 
            this.volumeStep.Location = new System.Drawing.Point(110, 16);
            this.volumeStep.Name = "volumeStep";
            this.volumeStep.ReadOnly = true;
            this.volumeStep.Size = new System.Drawing.Size(39, 21);
            this.volumeStep.TabIndex = 6;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 18);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(92, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "Volume sensitivity";
            // 
            // btnConfigure
            // 
            this.btnConfigure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConfigure.Location = new System.Drawing.Point(415, 8);
            this.btnConfigure.Name = "btnConfigure";
            this.btnConfigure.Size = new System.Drawing.Size(100, 35);
            this.btnConfigure.TabIndex = 1;
            this.btnConfigure.Text = "Configure";
            // 
            // btnRefresh
            // 
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.Location = new System.Drawing.Point(521, 7);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(100, 35);
            this.btnRefresh.TabIndex = 3;
            this.btnRefresh.Text = "Refresh";
            // 
            // frmApplicationSelector
            // 
            this.ClientSize = new System.Drawing.Size(633, 327);
            this.Controls.Add(this.listViewApplications);
            this.Controls.Add(this.panelButtons);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "frmApplicationSelector";
            this.panelButtons.ResumeLayout(false);
            this.panelButtons.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.volumeStep)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ImageList applicationIcons;
        private System.Windows.Forms.ListView listViewApplications;
        private System.Windows.Forms.Panel panelButtons;
        private System.Windows.Forms.Button btnConfigure;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown volumeStep;
    }
}
