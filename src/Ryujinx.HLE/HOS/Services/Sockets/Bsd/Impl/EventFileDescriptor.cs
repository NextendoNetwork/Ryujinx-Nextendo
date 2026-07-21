using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl
{
    class EventFileDescriptor : IFileDescriptor
    {
        private ulong _value;
        private readonly EventFdFlags _flags;

        // type is not Lock due to Monitor class usage
        private readonly object _lock = new();

        // [Nextendo] deferred Bsd Poll (single-thread eventfd deadlock fix)
        // EventFileDescriptor has no Device/ServerBase reference. The Bsd ServerBase subscribes to this
        // static event so that, after an eventfd becomes readable, the Bsd server loop is woken to
        // re-check any deferred Poll waiting on this eventfd (e.g. a client library's wakeup eventfd). Null-safe.
        public static event Action OnAnyWrite;

        public bool Blocking { get => !_flags.HasFlag(EventFdFlags.NonBlocking); set => throw new NotSupportedException(); }

        public ManualResetEvent WriteEvent { get; }
        public ManualResetEvent ReadEvent { get; }

        public EventFileDescriptor(ulong value, EventFdFlags flags)
        {
            // FIXME: We should support blocking operations.
            // Right now they can't be supported because it would cause the
            // service to lock up as we only have one thread processing requests.
            flags |= EventFdFlags.NonBlocking;

            _value = value;
            _flags = flags;

            WriteEvent = new ManualResetEvent(false);
            ReadEvent = new ManualResetEvent(false);
            UpdateEventStates();
            Logger.Info?.Print(LogClass.ServiceBsd, $"[DIAG] eventfd CREATED value={value} flags={flags}");
        }

        public int Refcount { get; set; }

        public void Dispose()
        {
            WriteEvent.Dispose();
            ReadEvent.Dispose();
        }

        private void ResetEventStates()
        {
            WriteEvent.Reset();
            ReadEvent.Reset();
        }

        private void UpdateEventStates()
        {
            if (_value > 0)
            {
                ReadEvent.Set();
            }

            if (_value != uint.MaxValue - 1)
            {
                WriteEvent.Set();
            }
        }

        public LinuxError Read(out int readSize, Span<byte> buffer)
        {
            if (buffer.Length < sizeof(ulong))
            {
                readSize = 0;

                return LinuxError.EINVAL;
            }

            lock (_lock)
            {
                ResetEventStates();

                ref ulong count = ref MemoryMarshal.Cast<byte, ulong>(buffer)[0];

                if (_value == 0)
                {
                    if (Blocking)
                    {
                        while (_value == 0)
                        {
                            Monitor.Wait(_lock);
                        }
                    }
                    else
                    {
                        readSize = 0;

                        UpdateEventStates();
                        return LinuxError.EAGAIN;
                    }
                }

                readSize = sizeof(ulong);

                if (_flags.HasFlag(EventFdFlags.Semaphore))
                {
                    --_value;

                    count = 1;
                }
                else
                {
                    count = _value;

                    _value = 0;
                }

                UpdateEventStates();
                return LinuxError.SUCCESS;
            }
        }

        public LinuxError Write(out int writeSize, ReadOnlySpan<byte> buffer)
        {
            if (!MemoryMarshal.TryRead(buffer, out ulong count) || count == ulong.MaxValue)
            {
                writeSize = 0;

                return LinuxError.EINVAL;
            }

            lock (_lock)
            {
                ResetEventStates();

                if (_value > _value + count)
                {
                    if (Blocking)
                    {
                        Monitor.Wait(_lock);
                    }
                    else
                    {
                        writeSize = 0;

                        UpdateEventStates();
                        return LinuxError.EAGAIN;
                    }
                }

                writeSize = sizeof(ulong);

                _value += count;
                Monitor.Pulse(_lock);

                UpdateEventStates();
                Logger.Info?.Print(LogClass.ServiceBsd, $"[DIAG] eventfd WRITE +{count} -> value={_value} (ReadEvent set, should wake poller)");
            }

            // [Nextendo] deferred Bsd Poll (single-thread eventfd deadlock fix)
            // Now that the eventfd is readable, wake the Bsd server loop so a deferred Poll waiting on
            // this eventfd is re-checked promptly (rather than waiting for the loop's 50ms tick).
            // Invoked OUTSIDE the lock and from a possibly-foreign thread: it only signals a KEvent,
            // it must never touch the deferred list. Null-safe.
            OnAnyWrite?.Invoke();

            return LinuxError.SUCCESS;
        }
    }
}
