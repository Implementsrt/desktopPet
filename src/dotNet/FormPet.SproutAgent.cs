using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DesktopPet
{
    public partial class FormPet
    {
        private const string AgentPipeName = "DesktopPet.Sprout.v1";
        private const string AgentSit = "sit", AgentFireworks = "fireworks";
        private NamedPipeServerStream agentPipe;

        private void AddAgentMenu(ContextMenuStrip menu)
        {
            var item = new ToolStripMenuItem("启用本地 Agent 接口");
            menu.Opening += (s, e) => item.Checked = agentPipe != null;
            item.Click += (s, e) =>
            {
                if (agentPipe != null) StopAgentPipe();
                else
                {
                    try { StartAgentPipe(); }
                    catch (IOException) { MessageBox.Show("本地 Agent 接口正在被占用。", "小芽"); }
                    catch (UnauthorizedAccessException) { MessageBox.Show("Windows 无法打开本地 Agent 接口。", "小芽"); }
                }
                item.Checked = agentPipe != null;
            };
            menu.Items.Add(item);
        }

        private void StartAgentPipe()
        {
            var rights = new PipeSecurity();
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                rights.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            rights.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            var pipe = new NamedPipeServerStream(AgentPipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 512, 512, rights);
            agentPipe = pipe;
            _ = Task.Run(() => ServeAgentPipe(pipe));
        }

        private void StopAgentPipe()
        {
            var pipe = agentPipe;
            agentPipe = null;
            pipe?.Dispose();
        }

        private async Task ServeAgentPipe(NamedPipeServerStream pipe)
        {
            try
            {
                while (true)
                {
                    await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                    try
                    {
                        Task<string> request = ReadAgentCommand(pipe);
                        string command = await ReadWithDeadline(pipe, request).ConfigureAwait(false);
                        if (command == null) continue;
                        string result = await DispatchAgentCommand(pipe, command).ConfigureAwait(false);
                        byte[] reply = Encoding.UTF8.GetBytes(result + "\n");
                        await pipe.WriteAsync(reply, 0, reply.Length).ConfigureAwait(false);
                        // Keep the response available until the client reads it and closes, or the deadline passes.
                        await ReadWithDeadline(pipe, ReadAgentCommand(pipe)).ConfigureAwait(false);
                    }
                    catch (IOException) { }
                    finally
                    {
                        // A closed client can put the stream in Broken state, where IsConnected is false.
                        try { pipe.Disconnect(); }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }
            finally
            {
                pipe.Dispose();
                try { BeginInvoke((Action)(() => { if (agentPipe == pipe) agentPipe = null; })); }
                catch (InvalidOperationException) { }
            }
        }

        private static async Task<string> ReadWithDeadline(NamedPipeServerStream pipe, Task<string> read)
        {
            if (await Task.WhenAny(read, Task.Delay(2000)).ConfigureAwait(false) == read)
                return await read.ConfigureAwait(false);
            pipe.Disconnect();
            try { await read.ConfigureAwait(false); }
            catch (IOException) { }
            return null;
        }

        private static async Task<string> ReadAgentCommand(NamedPipeServerStream pipe)
        {
            var buffer = new byte[80];
            int received = 0;
            while (received < buffer.Length)
            {
                int count = await pipe.ReadAsync(buffer, received, buffer.Length - received).ConfigureAwait(false);
                if (count == 0) return null;
                int newline = Array.IndexOf(buffer, (byte)'\n', received, count);
                if (newline >= 0) return Encoding.ASCII.GetString(buffer, 0, newline).TrimEnd('\r');
                received += count;
            }
            return "";
        }

        private Task<string> DispatchAgentCommand(NamedPipeServerStream pipe, string command)
        {
            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke((Action)(() => reply.TrySetResult(agentPipe == pipe && !Disposing
                ? ExecuteAgentCommand(command) : AgentError("disabled"))));
            return reply.Task;
        }

        private string ExecuteAgentCommand(string command)
        {
            if (command == "capabilities")
                return new JavaScriptSerializer().Serialize(new { ok = true, version = 1, actions = new[] { AgentSit, AgentFireworks } });
            if (command == "status")
                return new JavaScriptSerializer().Serialize(new
                {
                    ok = true, version = 1, personality = Personality.Name,
                    state = CurrentAnimation.ID == SproutSleep ? "sleeping" : climbWindow != IntPtr.Zero ? "climbing" :
                        IsDragging ? "dragging" : IsTossing ? "airborne" : SkillMovesSprout ? "playing" : "awake",
                    onWindow = hwndWindow != IntPtr.Zero
                });
            if (command != AgentSit && command != AgentFireworks) return AgentError("unknown_command");
            double now = motionClock.Elapsed.TotalMilliseconds;
            if (IsDragging || IsTossing || IsLeaving || climbWindow != IntPtr.Zero || sproutSkill != SproutSkill.None ||
                CurrentAnimation.ID == Animations.AnimationKill || readSessionIdle() >= QuietAfter)
                return AgentError("busy");
            if (command == AgentSit)
            {
                if (hwndWindow == IntPtr.Zero || !CanSupportPet(hwndWindow)) return AgentError("needs_window");
                SitOnWindow(now);
            }
            else
            {
                if (now < skillAllowedAt) return AgentError("cooldown");
                BeginSproutSkill(SproutSkill.Fireworks, Cursor.Position);
            }
            return "{\"ok\":true}";
        }

        private static string AgentError(string code) => "{\"ok\":false,\"error\":\"" + code + "\"}";
    }
}
