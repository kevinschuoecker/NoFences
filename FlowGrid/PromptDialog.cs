using System.Drawing;
using System.Windows.Forms;

namespace FlowGrid
{
    /// <summary>
    /// Small generic text prompt, built in code so no designer resources are needed.
    /// </summary>
    public class PromptDialog : Form
    {
        private readonly TextBox textBox;

        public string Value => textBox.Text;

        public PromptDialog(string title, string description, string initialValue)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 130);
            Font = new Font("Segoe UI", 9f);

            var label = new Label
            {
                Text = description,
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(396, 34)
            };

            textBox = new TextBox
            {
                Text = initialValue,
                Location = new Point(12, 52),
                Size = new Size(396, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(252, 90),
                Size = new Size(75, 27)
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(333, 90),
                Size = new Size(75, 27)
            };

            Controls.Add(label);
            Controls.Add(textBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }
    }
}
