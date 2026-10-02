using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System.Collections.Generic;
using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl
{
    class ManagedSocketPollManager : IPollManager
    {
        private static ManagedSocketPollManager _instance;

        public static ManagedSocketPollManager Instance
        {
            get
            {
                _instance ??= new ManagedSocketPollManager();

                return _instance;
            }
        }

        public bool IsCompatible(PollEvent evnt)
        {
            return evnt.FileDescriptor is ManagedSocket;
        }

        public LinuxError Poll(List<PollEvent> events, int timeoutMilliseconds, out int updatedCount)
        {
            List<ISocketImpl> readEvents = [];
            List<ISocketImpl> writeEvents = [];
            List<ISocketImpl> errorEvents = [];

            updatedCount = 0;

            foreach (PollEvent evnt in events)
            {
                if (evnt.FileDescriptor is ManagedSocket ms)
                {
                    bool isValidEvent = evnt.Data.InputEvents == 0;

                    errorEvents.Add(ms.Socket);

                    if ((evnt.Data.InputEvents & PollEventTypeMask.Input) != 0)
                    {
                        readEvents.Add(ms.Socket);

                        isValidEvent = true;
                    }

                    if ((evnt.Data.InputEvents & PollEventTypeMask.UrgentInput) != 0)
                    {
                        readEvents.Add(ms.Socket);

                        isValidEvent = true;
                    }

                    if ((evnt.Data.InputEvents & PollEventTypeMask.Output) != 0)
                    {
                        writeEvents.Add(ms.Socket);

                        isValidEvent = true;
                    }

                    if (!isValidEvent)
                    {
                        Logger.Warning?.Print(LogClass.ServiceBsd, $"Unsupported Poll input event type: {evnt.Data.InputEvents}");
                        return LinuxError.EINVAL;
                    }
                }
            }

            try
            {
                int actualTimeoutMicroseconds = timeoutMilliseconds == -1 ? -1 : timeoutMilliseconds * 1000;

                SocketHelpers.Select(readEvents, writeEvents, errorEvents, actualTimeoutMicroseconds);
            }
            catch (SocketException exception)
            {
                return WinSockHelper.ConvertError((WsaError)exception.ErrorCode);
            }

            foreach (PollEvent evnt in events)
            {
                if (evnt.FileDescriptor is ManagedSocket ms)
                {
                    ISocketImpl socket = ms.Socket;

                    // [Nextendo] revents se recalcule A NEUF a chaque appel, comme le fait poll().
                    // L'ancien `evnt.Data.OutputEvents & ~evnt.Data.InputEvents` recopiait le revents que
                    // l'invite avait laisse dans son tableau pollfd (il est relu de la memoire invitee a
                    // chaque appel, IClient.cs) : sans consequence tant que le compte venait de la taille
                    // des listes, mais avec le comptage POSIX ci-dessous un seul bit perime suffirait a
                    // rendre « pret » en boucle serree. On ne conserve que POLLNVAL, pose par IClient pour
                    // un descripteur mort.
                    PollEventTypeMask outputEvents = evnt.Data.OutputEvents & PollEventTypeMask.Invalid;

                    if (errorEvents.Contains(ms.Socket))
                    {
                        outputEvents |= PollEventTypeMask.Error;

                        // POSIX may report an ICMP port-unreachable error for a UDP
                        // hole-punch probe. Socket.Connected is false for the managed
                        // datagram socket, but that is not a peer disconnect and must
                        // not be exposed to Pia as POLLHUP.
                        if (socket.SocketType == SocketType.Stream &&
                            (!socket.Connected || !socket.IsBound))
                        {
                            outputEvents |= PollEventTypeMask.Disconnected;
                        }
                    }

                    // [Nextendo] Le bloc ci-dessus est du code mort sur Windows : errorEvents ne survit au
                    // Socket.Select que si l'hote a signale le socket dans exceptfds, et select() Windows ne
                    // le fait QUE pour des donnees OOB et pour un connect() rate -- JAMAIS pour un FIN/RST sur
                    // une connexion etablie. POLLHUP n'etait donc jamais emis : un socket dont le pair a
                    // raccroche etait sonde indefiniment avec revents = 0 (mesure : 142 sondages consecutifs,
                    // 38,8 s, sur le socket gRPC d'un jeu en ligne, pendant que le jeu croyait encore ecrire
                    // dessus). POSIX rapporte POLLERR/POLLHUP independamment des events demandes -- c'est deja
                    // ce que fait IClient.cs pour POLLNVAL. On interroge donc le socket directement.
                    if (IsPeerHungUp(socket, out bool hardError))
                    {
                        // Notification A FRONT : une seule fois par socket. Voir ManagedSocket.HangupReported —
                        // un POLLHUP a niveau sur un descripteur mort que l'invite garde sans interet en
                        // lecture ferait rendre chaque sondage instantanement, donc une boucle folle.
                        if (!ms.HangupReported)
                        {
                            ms.HangupReported = true;

                            outputEvents |= PollEventTypeMask.Disconnected;

                            if (hardError)
                            {
                                outputEvents |= PollEventTypeMask.Error;
                            }

                            Logger.Debug?.Print(LogClass.ServiceBsd,
                                $"[Nextendo] POLLHUP synthetise fd={evnt.Data.SocketFd} req={evnt.Data.InputEvents} hardError={hardError}");
                        }
                    }
                    else
                    {
                        ms.HangupReported = false;
                    }

                    if (readEvents.Contains(ms.Socket))
                    {
                        if ((evnt.Data.InputEvents & PollEventTypeMask.Input) != 0)
                        {
                            outputEvents |= PollEventTypeMask.Input;
                        }
                    }

                    if (writeEvents.Contains(ms.Socket))
                    {
                        outputEvents |= PollEventTypeMask.Output;
                    }

                    evnt.Data.OutputEvents = outputEvents;
                }
            }

            // [Nextendo] poll() rend le NOMBRE D'ENTREES dont revents est non nul. L'ancien calcul
            // (readEvents.Count + writeEvents.Count + errorEvents.Count) comptait deux fois un socket a la
            // fois lisible et inscriptible, comptait un socket demande en POLLPRI seul alors que revents
            // reste 0 pour lui (poll rendait « pret » sans rien a lire), et surtout ne comptait PAS une
            // entree qui ne porte qu'un POLLHUP synthetise -- ce qui aurait renvoye 0, donc ETIMEDOUT, donc
            // un revents que le client ignore.
            updatedCount = 0;

            foreach (PollEvent evnt in events)
            {
                if (evnt.Data.OutputEvents != 0)
                {
                    updatedCount++;
                }
            }

            return LinuxError.SUCCESS;
        }

        // [Nextendo] « Le pair a-t-il raccroche ? », repondu sans dependre de exceptfds.
        // Garde-fous, car un faux positif ferait abandonner une connexion saine :
        //  - sockets STREAM uniquement (un socket UDP repond aussi lisible/Available=0 sur un datagramme vide) ;
        //  - RemoteEndPoint non nul, donc un socket en ECOUTE est exclu (il repond lisible/Available=0 des
        //    qu'une connexion est en attente, ce qui n'est pas un raccrochage) ;
        //  - Connected == false : c'est le discriminant REELLEMENT MESURE. Sur les trois journaux du
        //    2026-08-12, la signature « lisible + Available=0 + Connected=false » apparait 142 fois dans la
        //    session qui decroche et 0 fois dans les deux saines, et la signature « lisible + Available=0 +
        //    Connected=true » n'apparait JAMAIS. L'exiger ne coute donc aucun cas reel et ecarte la
        //    demi-fermeture (le pair envoie FIN, la connexion reste utilisable en ecriture), pour laquelle
        //    le materiel reel ne signale pas non plus POLLHUP mais POLLIN avec un recv() a zero — ce que la
        //    branche readEvents ci-dessus fait deja quand le jeu demande POLLIN ;
        //  - une SocketException (Available leve WSAECONNRESET apres un RST) est un raccrochage dur.
        private static bool IsPeerHungUp(ISocketImpl socket, out bool hardError)
        {
            hardError = false;

            try
            {
                if (socket == null || socket.SocketType != SocketType.Stream || socket.RemoteEndPoint == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            try
            {
                return !socket.Connected && socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
            }
            catch (SocketException)
            {
                hardError = true;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public LinuxError Select(List<PollEvent> events, int timeout, out int updatedCount)
        {
            List<ISocketImpl> readEvents = [];
            List<ISocketImpl> writeEvents = [];
            List<ISocketImpl> errorEvents = [];

            updatedCount = 0;

            foreach (PollEvent pollEvent in events)
            {
                if (pollEvent.FileDescriptor is ManagedSocket ms)
                {
                    if (pollEvent.Data.InputEvents.HasFlag(PollEventTypeMask.Input))
                    {
                        readEvents.Add(ms.Socket);
                    }

                    if (pollEvent.Data.InputEvents.HasFlag(PollEventTypeMask.Output))
                    {
                        writeEvents.Add(ms.Socket);
                    }

                    if (pollEvent.Data.InputEvents.HasFlag(PollEventTypeMask.Error))
                    {
                        errorEvents.Add(ms.Socket);
                    }
                }
            }

            SocketHelpers.Select(readEvents, writeEvents, errorEvents, timeout);

            updatedCount = readEvents.Count + writeEvents.Count + errorEvents.Count;

            foreach (PollEvent pollEvent in events)
            {
                if (pollEvent.FileDescriptor is ManagedSocket ms)
                {
                    if (readEvents.Contains(ms.Socket))
                    {
                        pollEvent.Data.OutputEvents |= PollEventTypeMask.Input;
                    }

                    if (writeEvents.Contains(ms.Socket))
                    {
                        pollEvent.Data.OutputEvents |= PollEventTypeMask.Output;
                    }

                    if (errorEvents.Contains(ms.Socket))
                    {
                        pollEvent.Data.OutputEvents |= PollEventTypeMask.Error;
                    }
                }
            }

            return LinuxError.SUCCESS;
        }
    }
}
