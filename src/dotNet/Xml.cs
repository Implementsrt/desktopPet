using System;
using System.Xml;
using System.IO;
using System.Windows.Forms;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Schema;
using System.Globalization;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;

namespace DesktopPet
{
    
        /// <summary>
        /// Xml class contains all functions to read the XML file and functions to parse it.
        /// </summary>
    public sealed class Xml : IDisposable
    {
            /// <summary>
            /// XML Document, containing the animations xml.
            /// </summary>
        public XmlData.RootNode AnimationXML;

            /// <summary>
            /// XML String, used for the current running animation.
            /// </summary>
        public string AnimationXMLString;

            /// <summary>
            /// List of sprite images for animations.
            /// </summary>
        public IList<Bitmap> sprites;

            /// <summary>
            /// Width of sprite in pixels.
            /// </summary>
        public int spriteWidth;

            /// <summary>
            /// Height of sprite in pixels.
            /// </summary>
        public int spriteHeight;

            /// <summary>
            /// A memory stream containing the animation icon. This is visible in the taskbar and tray icon.
            /// </summary>
        public MemoryStream bitmapIcon;
            
            /// <summary>
            /// X position of the parent image. Used to set the child position.
            /// </summary>
        public int parentX;
            /// <summary>
            /// Y position of the parent image. Used to set the child position.
            /// </summary>
        public int parentY;
            /// <summary>
            /// If the parent is flipped. If so, the image will be flipped and screen-mirrored.
            /// </summary>
        public bool parentFlipped;
            /// <summary>
            /// Random spawn, this value changes each time the XML is reloaded. Used in the animation xml.
            /// </summary>
        int iRandomSpawn = 10;
            /// <summary>
            /// Scale the pet on HD monitors.
            /// </summary>
        int iScale = 1;

            /// <summary>
            /// Constructor. Initialize member variables.
            /// </summary>
        public Xml(int scaleFactor = 1)
        {
            sprites = new List<Bitmap>();
            iScale = scaleFactor;

            parentX = -1;                   // -1 means it is not a child.
            parentY = -1;
            parentFlipped = false;

			Random rand = new Random();
            iRandomSpawn = rand.Next(10, 90);

		}

            /// <summary>
            /// Dispose class and created objects.
            /// </summary>
        public void Dispose()
        {
            bitmapIcon.Dispose();

            foreach (var sprite in sprites)
            {
                sprite?.Dispose();
            }
            sprites.Clear();
        }
        
            /// <summary>
            /// Event handler to check XML validity.
            /// </summary>
            /// <param name="sender">Caller as object.</param>
            /// <param name="e">Validation event values.</param>
        static void ValidationEventHandler(object sender, ValidationEventArgs e)
        {
            switch (e.Severity)
            {
                case XmlSeverityType.Error:
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "XSD validation: " + e.Message);
                    break;
                case XmlSeverityType.Warning:
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "XSD validation: " + e.Message);
                    break;
            }

        }

            /// <summary>
            /// This function will load the XML. If something can't be loaded as expected, the default XML will be loaded.
            /// </summary>
            /// <returns>true, if the XML was loaded successfully.</returns>
        internal static string DefaultPetXml
        {
            get
            {
#if PORTABLE
                using (var reader = new StreamReader(typeof(Xml).Assembly.GetManifestResourceStream("DesktopPet.SproutAnimations.xml")))
                    return reader.ReadToEnd();
#else
                return Properties.Resources.animations;
#endif
            }
        }

        public bool ReadXML()
        {
            var serializer = new XmlSerializer(typeof(XmlData.RootNode));
            string configuredXml = Program.MyData.GetXml();
            AnimationXML = null;
            if (!string.IsNullOrWhiteSpace(configuredXml))
            {
                try
                {
                    using (var reader = new StringReader(configuredXml))
                        AnimationXML = (XmlData.RootNode)serializer.Deserialize(reader);
                    if (AnimationXML?.Header == null || AnimationXML.Image == null)
                        throw new InvalidDataException("Animation header or image is missing.");
                    AnimationXMLString = configuredXml;
                }
                catch (Exception ex)
                {
                    AnimationXML = null;
                    StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "User XML error: " + ex);
                    if (configuredXml.Length > 100)
                        MessageBox.Show("无法读取桌宠动画：" + ex.Message, "动画文件", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (AnimationXML == null)
            {
                AnimationXMLString = DefaultPetXml;
                using (var reader = new StringReader(AnimationXMLString))
                    AnimationXML = (XmlData.RootNode)serializer.Deserialize(reader);
                Program.MyData.SetXml(AnimationXMLString, "");
            }

            Program.MyData.SetImages(AnimationXML.Image.Png);
            Program.MyData.SetIcon(AnimationXML.Header.Icon);
            try
            {
                ReadImages();
                if (AnimationXML.Header.Petname.Length > 16)
                    AnimationXML.Header.Petname = AnimationXML.Header.Petname.Substring(0, 16);
                return true;
            }
            catch (Exception ex)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Error reading XML: " + ex.Message);
                MessageBox.Show("无法加载桌宠素材，将恢复默认桌宠。", "动画文件", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                AnimationXML.Image.Png = string.Empty;
                AnimationXML.Header.Icon = string.Empty;
            }
        }

            /// <summary>
            /// Load the animations (read them from XML file)
            /// </summary>
            /// <param name="animations">Animation class where the animations should be saved</param>
        public void LoadAnimations(Animations animations)
        {
            if(AnimationXML.Animations == null)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "No animations for this pet");
                return;
            }
                // for each animation
            foreach (XmlData.AnimationNode node in AnimationXML.Animations.Animation)
            {
                TAnimation ani = animations.AddAnimation(node.Id, node.Id.ToString());
                ani.Border = node.Border != null;
                ani.Gravity = node.Gravity != null;

                ani.Name = node.Name;
                switch (ani.Name)
                {
                    case "fall": animations.AnimationFall = node.Id; break;
                    case "drag": animations.AnimationDrag = node.Id; break;
                    case "kill": animations.AnimationKill = node.Id; break;
                    case "sync": animations.AnimationSync = node.Id; break;
                    case "toss": animations.AnimationToss = node.Id; break;
                    case "fall soft": animations.AnimationFallSoft = node.Id; break;
                    case "fall hard": animations.AnimationFallHard = node.Id; break;
                }

                ani.Start.X = GetXMLCompute(node.Start.X, "animation " + node.Id + ": node.start.X");
                ani.Start.Y = GetXMLCompute(node.Start.Y, "animation " + node.Id + ": node.start.Y");
                ani.Start.Interval = GetXMLCompute(node.Start.Interval, "animation " + node.Id + ": node.start.Interval");
                ani.Start.OffsetY = node.Start.OffsetY;
                ani.Start.Opacity = node.Start.Opacity;

                ani.End.X = GetXMLCompute(node.End.X, "animation " + node.Id + ": node.end.X");
                ani.End.Y = GetXMLCompute(node.End.Y, "animation " + node.Id + ": node.end.Y");
                ani.End.Interval = GetXMLCompute(node.End.Interval, "animation " + node.Id + ": node.end.Interval");
                ani.End.OffsetY = node.End.OffsetY;
                ani.End.Opacity = node.End.Opacity;

                ani.Sequence.RepeatFrom = node.Sequence.RepeatFromFrame;
                ani.Sequence.Action = node.Sequence.Action;
                ani.Sequence.Repeat = GetXMLCompute(node.Sequence.RepeatCount, "animation " + node.Id + ": node.sequence.Repeat");
                ani.Sequence.Frames.AddRange(node.Sequence.Frame);
                if (ani.Sequence.RepeatFrom > 0)
                    ani.Sequence.TotalSteps = ani.Sequence.Frames.Count + (ani.Sequence.Frames.Count - ani.Sequence.RepeatFrom - 1) * ani.Sequence.Repeat.Value;
                else
                    ani.Sequence.TotalSteps = ani.Sequence.Frames.Count + ani.Sequence.Frames.Count * ani.Sequence.Repeat.Value;
                if (node.Sequence.Next != null)
                {
                    foreach (XmlData.NextNode nextNode in node.Sequence.Next)
                    {
                        TNextAnimation.TOnly where;
                        switch (nextNode.OnlyFlag)
                        {
                            case "taskbar": where = TNextAnimation.TOnly.TASKBAR; break;
                            case "window": where = TNextAnimation.TOnly.WINDOW; break;
                            case "windowSide": where = TNextAnimation.TOnly.WINDOWSIDE; break;
                            case "horizontal": where = TNextAnimation.TOnly.HORIZONTAL; break;
                            case "horizontal+": where = TNextAnimation.TOnly.HORIZONTAL_; break;
                            case "vertical": where = TNextAnimation.TOnly.VERTICAL; break;
                            case "vertical+": where = TNextAnimation.TOnly.VERTICAL_; break;
                            case "petSide": where = TNextAnimation.TOnly.PETSIDE; break;
                            default: where = TNextAnimation.TOnly.NONE; break;
                        }

                        ani.EndAnimation.Add(
                            new TNextAnimation(
                                nextNode.Value,
                                nextNode.Probability,
                                where
                            )
                        );
                    }
                }

                if (ani.Border)
                {
                    foreach (XmlData.NextNode nextNode in node.Border.Next)
                    {
                        TNextAnimation.TOnly where;
                        switch (nextNode.OnlyFlag)
                        {
                            case "taskbar": where = TNextAnimation.TOnly.TASKBAR; break;
                            case "window": where = TNextAnimation.TOnly.WINDOW; break;
                            case "windowSide": where = TNextAnimation.TOnly.WINDOWSIDE; break;
                            case "horizontal": where = TNextAnimation.TOnly.HORIZONTAL; break;
                            case "horizontal+": where = TNextAnimation.TOnly.HORIZONTAL_; break;
                            case "vertical": where = TNextAnimation.TOnly.VERTICAL; break;
                            case "vertical+": where = TNextAnimation.TOnly.VERTICAL_; break;
                            case "petSide": where = TNextAnimation.TOnly.PETSIDE; break;
                            default: where = TNextAnimation.TOnly.NONE; break;
                        }
                        ani.Border = true;
                        ani.EndBorder.Add(
                            new TNextAnimation(
                                nextNode.Value,
                                nextNode.Probability,
                                where
                            )
                        );
                    }
                }

                if (ani.Gravity)
                {
                    foreach (XmlData.NextNode nextNode in node.Gravity.Next)
                    {
                        TNextAnimation.TOnly where;
                        switch (nextNode.OnlyFlag)
                        {
                            case "taskbar": where = TNextAnimation.TOnly.TASKBAR; break;
                            case "window": where = TNextAnimation.TOnly.WINDOW; break;
                            case "windowSide": where = TNextAnimation.TOnly.WINDOWSIDE; break;
                            case "horizontal": where = TNextAnimation.TOnly.HORIZONTAL; break;
                            case "horizontal+": where = TNextAnimation.TOnly.HORIZONTAL_; break;
                            case "vertical": where = TNextAnimation.TOnly.VERTICAL; break;
                            case "vertical+": where = TNextAnimation.TOnly.VERTICAL_; break;
                            case "petSide": where = TNextAnimation.TOnly.PETSIDE; break;
                            default: where = TNextAnimation.TOnly.NONE; break;
                        }
                        ani.Gravity = true;
                        ani.EndGravity.Add(
                            new TNextAnimation(
                                nextNode.Value,
                                nextNode.Probability,
                                where
                            )
                        );
                    }
                }
                
                animations.SaveAnimation(ani, node.Id);
            }

            // for each spawn
            if (AnimationXML.Spawns.Spawn != null)
            {
                foreach (XmlData.SpawnNode node in AnimationXML.Spawns.Spawn)
                {
                    TSpawn ani = animations.AddSpawn(
                        node.Id,
                        node.Probability);

                    ani.Start.X = GetXMLCompute(node.X, "spawn " + node.Id + ": node.X");
                    ani.Start.Y = GetXMLCompute(node.Y, "spawn " + node.Id + ": node.X");
                    ani.Next = node.Next;

                    animations.SaveSpawn(ani, node.Id);
                }
            }

            // for each child
            if (AnimationXML.Childs?.Child != null)
            {
                foreach (XmlData.ChildNode node in AnimationXML.Childs.Child)
                {
                    TChild aniChild = animations.AddChild(node.Id);
                    aniChild.AnimationID = node.Id;

                    aniChild.Position.X = GetXMLCompute(node.X, "child " + node.Id + ": node.X");
                    aniChild.Position.Y = GetXMLCompute(node.Y, "child " + node.Id + ": node.Y");
                    aniChild.Next = node.Next;

                    animations.SaveChild(aniChild, node.Id);
                }
            }

            // for each sound
            if (AnimationXML.Sounds != null && AnimationXML.Sounds.Sound != null)
            {
                foreach (XmlData.SoundNode node in AnimationXML.Sounds.Sound)
                {
                    animations.AddSound(node.Id, node.Probability, node.Loop, node.Base64);
                }
            }
        }

            /// <summary>
            /// Get the value from XML file. If special keys are used (like screenW) it will be converted.
            /// </summary>
            /// <param name="text">XML text value.</param>
            /// <param name="debugInfo">Info text to show if this function fails.</param>
            /// <returns>A structure with the values.</returns>
        public TValue GetXMLCompute(string text, string debugInfo)
        {
            TValue v;

            v.Compute = text;
            v.IsDynamic = (v.Compute.IndexOf("random") >= 0 || v.Compute.IndexOf("randS") >= 0 || v.Compute.IndexOf("imageX") >= 0 || v.Compute.IndexOf("imageY") >= 0);
            v.IsScreen = (v.Compute.IndexOf("screen") >= 0 || v.Compute.IndexOf("area") >= 0);
            v.Value = ParseValue(v.Compute, debugInfo);

            return v;
        }

        /// <summary>
        /// Parse a value, converting keys like screenW, imageH, random,... to integers.
        /// </summary>
        /// <remarks>
        /// See <a href="https://msdn.microsoft.com/en-us/library/9za5w1xw(v=vs.100).aspx">https://msdn.microsoft.com/en-us/library/9za5w1xw(v=vs.100).aspx</a>
        /// for more information of what you can write as sText (expression to compute).
        /// </remarks> 
        /// <param name="parsingText">The text to parse and convert.</param>
        /// <param name="debugInfo">Debug text to show if this function fails.</param>
        /// <param name="screenIndex">If set, the xml will be parsed with the screen dimension.</param>
        /// <returns>The integer value from the parsed text expression.</returns>
        public int ParseValue(string parsingText, string debugInfo, int screenIndex = -1)
        {
            int iRet = 0;
            DataTable dt = new DataTable();
            Random rand = new Random();

                // When adding a child, it is important to place the child on the other side of the parent, if the parent is flipped.
            if(parentFlipped)
            {
                if (parsingText.IndexOf("-imageW") >= 0)
                {
                    parsingText = parsingText.Replace("-imageW", "+imageW");
                }
                else
                {
                    parsingText = parsingText.Replace("imageW", "(-imageW)");
                }
            }
            var screen = Screen.PrimaryScreen;
            Screen[] screens = Screen.AllScreens;
            if (screenIndex >= 0 && screenIndex < screens.Length) screen = screens[screenIndex];

            parsingText = parsingText.Replace("screenW", screen.Bounds.Width.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("screenH", screen.Bounds.Height.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("areaW", screen.WorkingArea.Width.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("areaH", (screen.WorkingArea.Height + screen.WorkingArea.Y).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageW", spriteWidth.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageH", spriteHeight.ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageX", (parentX).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("imageY", (parentY).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("random", rand.Next(0, 100).ToString(CultureInfo.InvariantCulture));
            parsingText = parsingText.Replace("randS", iRandomSpawn.ToString(CultureInfo.InvariantCulture));
			parsingText = parsingText.Replace("scale", Program.MyData.GetScale().ToString());

			var v = dt.Compute(parsingText, "");
			if (double.TryParse(v.ToString(), out double dv))
			{
				iRet = (int)dv;
			}
			else
			{
				StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.error, "Unable to parse integer: " + parsingText + " - " + debugInfo);
			}

			return iRet;
        }

            /// <summary>
            /// Read images from XML file and store them in the application.
            /// </summary>
        private void ReadImages()
        {
            MemoryStream imageStream = null;

            try
            {
                if (Program.MyData.GetImages().Length < 2) throw new InvalidDataException();
                imageStream = new MemoryStream(Convert.FromBase64String(Program.MyData.GetImages()));
                // only decode once so dont need to keep the source string for image
                Program.MyData.SetImages(string.Empty); 
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "user images loaded");
            }
            catch (Exception)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "user images not found, loading defaults");
                try
                {
                    string pngStr = AnimationXML.Image.Png;
                    int mod4 = pngStr.Length % 4;
                    if (mod4 > 0)
                    {
                        pngStr += new string('=', 4 - mod4);
                    }
                    Program.MyData.SetImages(pngStr);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                try
                {
                    imageStream = new MemoryStream(Convert.FromBase64String(Program.MyData.GetImages()));
                    // only decode once so dont need to keep the source string for image
                    Program.MyData.SetImages(string.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }
            
            try
            {
                if (Program.MyData.GetIcon().Length < 100) throw new InvalidDataException();
                bitmapIcon = new MemoryStream(Convert.FromBase64String(Program.MyData.GetIcon()));
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.info, "user icon loaded");
            }
            catch (Exception)
            {
                StartUp.AddDebugInfo(StartUp.DEBUG_TYPE.warning, "no user icon, loading default");
                try
                {
                    var strIco = AnimationXML.Header.Icon;
                    int mod4 = strIco.Length % 4;
                    if (mod4 > 0)
                    {
                        strIco += new string('=', 4 - mod4);
                    }
                    Program.MyData.SetIcon(strIco);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                try
                {
                    bitmapIcon = new MemoryStream(Convert.FromBase64String(Program.MyData.GetIcon()));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            using (imageStream)
            using (var image = new Bitmap(imageStream))
            {
                int sourceWidth = image.Width / AnimationXML.Image.TilesX;
                int sourceHeight = image.Height / AnimationXML.Image.TilesY;
                bool sproutSkins = IsSprout && AnimationXML.Image.Skins == 2 && sourceWidth == 152 && sourceHeight == 152;
                int baseWidth = sproutSkins ? 76 : sourceWidth;
                int baseHeight = sproutSkins ? 76 : sourceHeight;
                while (iScale > 1 && (baseWidth * iScale > 255 || baseHeight * iScale > 255)) iScale--;
                spriteWidth = baseWidth * iScale;
                spriteHeight = baseHeight * iScale;
                sprites = BuildSprites(image, sourceWidth, sourceHeight);
            }
        }

        /// <summary>
        /// Build sprites from animation image
        /// </summary>
        /// <param name="spriteSheet"></param>
        /// <returns></returns>
        internal bool IsSprout => AnimationXML?.Header?.Author == "DesktopPet Preview" && AnimationXML.Header.Petname == "小芽";

        private IList<Bitmap> BuildSprites(Bitmap spriteSheet, int sourceWidth, int sourceHeight)
        {
            var sprites = new List<Bitmap>();

            for (var yOffset = 0; yOffset < spriteSheet.Height; yOffset += sourceHeight)
            {
                for (var xOffset = 0; xOffset < spriteSheet.Width; xOffset += sourceWidth)
                {
                    var source = new Rectangle(xOffset, yOffset, sourceWidth, sourceHeight);
                    if (spriteWidth == sourceWidth && spriteHeight == sourceHeight)
                    {
                        sprites.Add(spriteSheet.Clone(source, spriteSheet.PixelFormat));
                        continue;
                    }
                    using (var tile = spriteSheet.Clone(source, spriteSheet.PixelFormat))
                    {
                        var frame = new Bitmap(spriteWidth, spriteHeight, PixelFormat.Format32bppArgb);
                        using (var graphics = Graphics.FromImage(frame))
                        {
                            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                            graphics.PixelOffsetMode = PixelOffsetMode.Half;
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.DrawImage(tile, new Rectangle(0, 0, spriteWidth, spriteHeight),
                                new Rectangle(0, 0, tile.Width, tile.Height), GraphicsUnit.Pixel);
                        }
                        sprites.Add(frame);
                    }
                }
            }
            return sprites;
        }
    }
}
