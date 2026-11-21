use std::collections::{HashMap, VecDeque};
use std::sync::Mutex;

const HISTORY_MS: f64 = 10_000.0;

struct PidTrack {
    presents: VecDeque<f64>,
    last_seen_host_ms: f64,
}

pub struct FrameStore {
    tracks: Mutex<HashMap<i32, PidTrack>>,
}

impl Default for FrameStore {
    fn default() -> Self {
        Self::new()
    }
}

impl FrameStore {
    pub fn new() -> Self {
        Self {
            tracks: Mutex::new(HashMap::new()),
        }
    }

    pub fn clear(&self) {
        if let Ok(mut guard) = self.tracks.lock() {
            guard.clear();
        }
    }

    pub fn record(&self, pid: i32, timestamp_ms: f64, host_now_ms: f64) {
        if let Ok(mut guard) = self.tracks.lock() {
            let track = guard.entry(pid).or_insert_with(|| PidTrack {
                presents: VecDeque::with_capacity(256),
                last_seen_host_ms: host_now_ms,
            });
            track.presents.push_back(timestamp_ms);
            while let Some(&front) = track.presents.front() {
                if front < timestamp_ms - HISTORY_MS {
                    track.presents.pop_front();
                } else {
                    break;
                }
            }
            track.last_seen_host_ms = host_now_ms;
        }
    }

    pub fn query(
        &self,
        pid: i32,
        host_now_ms: f64,
        max_recent: usize,
        recent_out: &mut Vec<f64>,
    ) -> Option<(f64, f64, f64)> {
        let guard = self.tracks.lock().ok()?;
        let track = guard.get(&pid)?;
        if track.presents.len() < 3 || (host_now_ms - track.last_seen_host_ms) > 2000.0 {
            return None;
        }

        let ts: Vec<f64> = track.presents.iter().copied().collect();
        drop(guard);

        let metrics = compute_metrics_slice(&ts)?;
        recent_out.clear();
        let ft_count = ts.len().saturating_sub(1);
        let take = ft_count.min(max_recent);
        let start = ts.len() - take;
        for i in start..ts.len() {
            recent_out.push(ts[i] - ts[i - 1]);
        }
        Some(metrics)
    }
}

pub fn compute_metrics_slice(ts: &[f64]) -> Option<(f64, f64, f64)> {
    if ts.len() < 3 {
        return None;
    }

    let n = ts.len() - 1;
    let mut frame_times = Vec::with_capacity(n);
    for i in 0..n {
        frame_times.push(ts[i + 1] - ts[i]);
    }

    let last = ts[ts.len() - 1];
    let cutoff = last - 1000.0;
    let first = ts.iter().position(|&x| x >= cutoff).unwrap_or(0);
    let span = last - ts[first];
    let frames = (ts.len() - 1 - first) as f64;
    let fps = if span > 0.0 {
        frames * 1000.0 / span
    } else {
        0.0
    };

    frame_times.sort_unstable_by(f64::total_cmp);
    let p99_idx = ((n as f64 * 0.99).floor() as usize).min(n - 1);
    let p99 = frame_times[p99_idx];
    let low1 = if p99 > 0.0 { 1000.0 / p99 } else { 0.0 };
    let ft_ms = if fps > 0.0 { 1000.0 / fps } else { 0.0 };

    Some((fps, ft_ms, low1))
}
