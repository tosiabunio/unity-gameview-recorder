using System;
using System.Reflection;

namespace Airon.GameViewRecorder
{
    /// <summary>
    /// Reads the final mix of the Wwise sound engine, for projects that play their sound through
    /// Wwise (typically with Unity audio disabled). Bound by reflection, so the package doesn't depend
    /// on the Wwise integration. It uses the integration's device capture on the main output
    /// (StartDeviceCapture, UpdateCaptureSampleCount, GetCaptureSamples on AkUnitySoundEngine, or
    /// AkSoundEngine in older integrations): Wwise buffers what it renders as interleaved floats
    /// until it is read. Main thread.
    /// </summary>
    sealed class WwiseCapture
    {
        static readonly string[] EngineTypeNames = { "AkUnitySoundEngine", "AkSoundEngine" };

        readonly ulong _output;
        readonly Func<ulong, uint> _available;
        readonly Func<ulong, float[], uint, uint> _read;
        readonly MethodInfo _stop;
        readonly MethodInfo _clear;

        WwiseCapture(ulong output, int sampleRate, int channels, Func<ulong, uint> available,
            Func<ulong, float[], uint, uint> read, MethodInfo stop, MethodInfo clear)
        {
            _output = output;
            SampleRate = sampleRate;
            Channels = channels;
            _available = available;
            _read = read;
            _stop = stop;
            _clear = clear;
        }

        public int SampleRate { get; }

        public int Channels { get; }

        /// <summary>Interleaved samples from the last <see cref="Read"/>.</summary>
        public float[] Samples { get; private set; } = Array.Empty<float>();

        /// <summary>The Wwise sound engine class, or null when the project has no Wwise.</summary>
        public static Type FindEngine()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var name in EngineTypeNames)
            {
                foreach (var assembly in assemblies)
                {
                    var type = assembly.GetType(name, false);
                    if (type != null)
                        return type;
                }
            }
            return null;
        }

        /// <summary>
        /// Starts capturing the main output of <paramref name="engine"/> (see <see cref="FindEngine"/>).
        /// Null, with the reason in <paramref name="problem"/>, when there is no Wwise, its sound
        /// engine isn't running, or the integration has no device capture.
        /// </summary>
        public static WwiseCapture TryStart(Type engine, out string problem)
        {
            problem = null;
            if (engine == null)
            {
                problem = "Wwise isn't in this project.";
                return null;
            }

            try
            {
                var available = Bind<Func<ulong, uint>>(engine, "UpdateCaptureSampleCount", typeof(ulong));
                var read = Bind<Func<ulong, float[], uint, uint>>(engine, "GetCaptureSamples", typeof(ulong), typeof(float[]), typeof(uint));
                var start = Find(engine, "StartDeviceCapture", typeof(ulong));
                var stop = Find(engine, "StopDeviceCapture", typeof(ulong));
                var isInitialized = Find(engine, "IsInitialized");
                var sampleRate = Find(engine, "GetSampleRate");
                var outputId = Find(engine, "GetOutputID", typeof(uint), typeof(uint));
                if (available == null || read == null || start == null || stop == null || isInitialized == null || sampleRate == null || outputId == null)
                {
                    problem = $"This Wwise integration ({engine.Name}) has no device capture.";
                    return null;
                }

                if (!(bool)isInitialized.Invoke(null, null))
                {
                    problem = "The Wwise sound engine isn't initialized.";
                    return null;
                }

                int rate = Convert.ToInt32(sampleRate.Invoke(null, null));
                if (rate <= 0)
                {
                    problem = "Wwise reports no sample rate.";
                    return null;
                }

                // Share set 0, device 0: the main output.
                ulong output = (ulong)outputId.Invoke(null, new object[] { 0u, 0u });
                var clear = Find(engine, "ClearCaptureData");
                clear?.Invoke(null, null);
                start.Invoke(null, new object[] { output });
                return new WwiseCapture(output, rate, ChannelCount(engine, output), available, read, stop, clear);
            }
            catch (Exception e)
            {
                problem = $"Wwise capture failed to start: {(e as TargetInvocationException)?.InnerException?.Message ?? e.Message}";
                return null;
            }
        }

        /// <summary>Reads what Wwise rendered since the last call into <see cref="Samples"/>. Returns the number of samples.</summary>
        public int Read()
        {
            uint available = _available(_output);
            if (available == 0)
                return 0;

            if (Samples.Length < available)
                Samples = new float[Math.Max(available, (uint)Samples.Length * 2)];
            return (int)Math.Min(_read(_output, Samples, available), available);
        }

        public void Stop()
        {
            try
            {
                _stop.Invoke(null, new object[] { _output });
                _clear?.Invoke(null, null);
            }
            catch (Exception)
            {
                // The sound engine is already gone.
            }
        }

        static int ChannelCount(Type engine, ulong output)
        {
            object config = Find(engine, "GetSpeakerConfiguration", typeof(ulong))?.Invoke(null, new object[] { output });
            object count = config?.GetType().GetProperty("uNumChannels")?.GetValue(config)
                           ?? config?.GetType().GetField("uNumChannels")?.GetValue(config);
            int channels = count != null ? Convert.ToInt32(count) : 0;
            return channels > 0 ? channels : 2;
        }

        static MethodInfo Find(Type type, string name, params Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);

        static T Bind<T>(Type type, string name, params Type[] parameters) where T : Delegate
        {
            var method = Find(type, name, parameters);
            return method == null ? null : (T)Delegate.CreateDelegate(typeof(T), method, false);
        }
    }
}
