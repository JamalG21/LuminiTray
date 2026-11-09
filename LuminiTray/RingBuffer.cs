using System;

namespace LuminiTray;

/// <summary>
/// Buffer circular de alto desempenho, thread-safe, com pré-alocação estática (Zero GC).
/// Utilizado para acumular pacotes WASAPI de tamanhos variáveis e fornecer
/// blocos fixos de exatamente 480 amostras (10 ms a 48 kHz) para o RNNoise e saídas de áudio.
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _readPos;
    private int _writePos;
    private int _count;
    private readonly object _lock = new();

    public RingBuffer(int capacity)
    {
        _buffer = new T[Math.Max(capacity, 1)];
    }

    public int Capacity => _buffer.Length;
    public int Count
    {
        get
        {
            lock (_lock) return _count;
        }
    }

    public int Write(ReadOnlySpan<T> data)
    {
        lock (_lock)
        {
            int toWrite = Math.Min(data.Length, _buffer.Length - _count);
            for (int i = 0; i < toWrite; i++)
            {
                _buffer[_writePos] = data[i];
                _writePos = (_writePos + 1) % _buffer.Length;
            }
            _count += toWrite;
            return toWrite;
        }
    }

    public void WriteOverwrite(ReadOnlySpan<T> data)
    {
        lock (_lock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                _buffer[_writePos] = data[i];
                _writePos = (_writePos + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                {
                    _count++;
                }
                else
                {
                    _readPos = (_readPos + 1) % _buffer.Length;
                }
            }
        }
    }

    public int Read(Span<T> destination)
    {
        lock (_lock)
        {
            int toRead = Math.Min(destination.Length, _count);
            for (int i = 0; i < toRead; i++)
            {
                destination[i] = _buffer[_readPos];
                _readPos = (_readPos + 1) % _buffer.Length;
            }
            _count -= toRead;
            return toRead;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _readPos = 0;
            _writePos = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Limita o backlog acumulado na fila para evitar latência perpétua.
    /// Se a quantidade de amostras exceder maxAllowedCount, as amostras mais antigas são descartadas
    /// avançando a posição de leitura, garantindo fluxo estritamente em tempo real.
    /// </summary>
    public int TrimExcess(int maxAllowedCount)
    {
        if (maxAllowedCount < 0) maxAllowedCount = 0;
        lock (_lock)
        {
            if (_count > maxAllowedCount)
            {
                int discarded = _count - maxAllowedCount;
                _readPos = (_readPos + discarded) % _buffer.Length;
                _count = maxAllowedCount;
                return discarded;
            }
            return 0;
        }
    }
}
