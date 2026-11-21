use std::f64::consts::PI;

pub const SAMPLE_RATE: i32 = 48_000;
pub const FRAME_SAMPLES: usize = 480;
const LIMITER_KNEE: f32 = 0.85;
const DEESS_THRESHOLD: f32 = 0.06;

#[repr(C)]
#[derive(Clone, Copy)]
pub struct DspParams {
    pub suppression_intensity: f32,
    pub gain_db: f32,
    pub gate_threshold_db: f32,
    pub gate_release_ms: f32,
    pub gate_hysteresis_db: f32,
    pub gate_hold_ms: f32,
    pub vad_threshold: f32,
    pub vad_hold_ms: f32,
    pub low_eq_db: f32,
    pub mid_eq_db: f32,
    pub high_eq_db: f32,
    pub limiter_enabled: u8,
    pub deesser_enabled: u8,
}

#[derive(Clone, Copy)]
struct Biquad {
    b0: f32,
    b1: f32,
    b2: f32,
    a1: f32,
    a2: f32,
    x1: f32,
    x2: f32,
    y1: f32,
    y2: f32,
}

impl Biquad {
    fn high_pass(sr: i32, f: f64, q: f64) -> Self {
        let mut b = Self {
            b0: 1.0,
            b1: 0.0,
            b2: 0.0,
            a1: 0.0,
            a2: 0.0,
            x1: 0.0,
            x2: 0.0,
            y1: 0.0,
            y2: 0.0,
        };
        b.configure_high_pass(sr as f64, f, q);
        b
    }

    fn low_shelf(sr: i32, f: f64, db: f64) -> Self {
        let mut b = Self::high_pass(sr, f, 0.707);
        b.configure_low_shelf(sr as f64, f, db);
        b
    }

    fn high_shelf(sr: i32, f: f64, db: f64) -> Self {
        let mut b = Self::high_pass(sr, f, 0.707);
        b.configure_high_shelf(sr as f64, f, db);
        b
    }

    fn peaking(sr: i32, f: f64, q: f64, db: f64) -> Self {
        let mut b = Self::high_pass(sr, f, q);
        b.configure_peaking(sr as f64, f, q, db);
        b
    }

    #[inline]
    fn process(&mut self, x: f32) -> f32 {
        let y = self.b0 * x + self.b1 * self.x1 + self.b2 * self.x2
            - self.a1 * self.y1
            - self.a2 * self.y2;
        self.x2 = self.x1;
        self.x1 = x;
        self.y2 = self.y1;
        self.y1 = y;
        y
    }

    fn configure_high_pass(&mut self, sr: f64, f: f64, q: f64) {
        let w = 2.0 * PI * f / sr;
        let c = w.cos();
        let s = w.sin();
        let alpha = s / (2.0 * q);
        let b0 = (1.0 + c) / 2.0;
        let b1 = -(1.0 + c);
        let b2 = (1.0 + c) / 2.0;
        let a0 = 1.0 + alpha;
        let a1 = -2.0 * c;
        let a2 = 1.0 - alpha;

        self.b0 = (b0 / a0) as f32;
        self.b1 = (b1 / a0) as f32;
        self.b2 = (b2 / a0) as f32;
        self.a1 = (a1 / a0) as f32;
        self.a2 = (a2 / a0) as f32;
    }

    fn configure_peaking(&mut self, sr: f64, f: f64, q: f64, db: f64) {
        self.configure_eq(sr, f, q, db, 0);
    }

    fn configure_low_shelf(&mut self, sr: f64, f: f64, db: f64) {
        self.configure_eq(sr, f, 0.707, db, -1);
    }

    fn configure_high_shelf(&mut self, sr: f64, f: f64, db: f64) {
        self.configure_eq(sr, f, 0.707, db, 1);
    }

    fn configure_eq(&mut self, sr: f64, f: f64, q: f64, db: f64, shelf: i32) {
        let a = 10.0f64.powf(db / 40.0);
        let w = 2.0 * PI * f / sr;
        let c = w.cos();
        let s = w.sin();
        let alpha = s / (2.0 * q);
        let beta = a.sqrt() * alpha;

        let (b0, b1, b2, a0, a1, a2) = if shelf < 0 {
            (
                a * ((a + 1.0) - (a - 1.0) * c + 2.0 * beta),
                2.0 * a * ((a - 1.0) - (a + 1.0) * c),
                a * ((a + 1.0) - (a - 1.0) * c - 2.0 * beta),
                (a + 1.0) + (a - 1.0) * c + 2.0 * beta,
                -2.0 * ((a - 1.0) + (a + 1.0) * c),
                (a + 1.0) + (a - 1.0) * c - 2.0 * beta,
            )
        } else if shelf > 0 {
            (
                a * ((a + 1.0) + (a - 1.0) * c + 2.0 * beta),
                -2.0 * a * ((a - 1.0) + (a + 1.0) * c),
                a * ((a + 1.0) - (a - 1.0) * c - 2.0 * beta),
                (a + 1.0) - (a - 1.0) * c + 2.0 * beta,
                2.0 * ((a - 1.0) - (a + 1.0) * c),
                (a + 1.0) - (a - 1.0) * c - 2.0 * beta,
            )
        } else {
            (
                1.0 + alpha * a,
                -2.0 * c,
                1.0 - alpha * a,
                1.0 + alpha / a,
                -2.0 * c,
                1.0 - alpha / a,
            )
        };

        self.b0 = (b0 / a0) as f32;
        self.b1 = (b1 / a0) as f32;
        self.b2 = (b2 / a0) as f32;
        self.a1 = (a1 / a0) as f32;
        self.a2 = (a2 / a0) as f32;
    }
}

pub struct VoiceDspState {
    rumble_hpf: Biquad,
    low: Biquad,
    mid: Biquad,
    high: Biquad,
    sidechain_hpf: Biquad,
    sibilance_hpf: Biquad,
    gate_gain: f32,
    gate_is_open: bool,
    gate_hold_counter: i32,
    vad_grace_counter: i32,
    de_ess_env: f32,
}

impl Default for VoiceDspState {
    fn default() -> Self {
        Self::new()
    }
}

impl VoiceDspState {
    pub fn new() -> Self {
        Self {
            rumble_hpf: Biquad::high_pass(SAMPLE_RATE, 80.0, 0.707),
            low: Biquad::low_shelf(SAMPLE_RATE, 180.0, 0.0),
            mid: Biquad::peaking(SAMPLE_RATE, 1200.0, 0.8, 0.0),
            high: Biquad::high_shelf(SAMPLE_RATE, 6000.0, 0.0),
            sidechain_hpf: Biquad::high_pass(SAMPLE_RATE, 150.0, 0.707),
            sibilance_hpf: Biquad::high_pass(SAMPLE_RATE, 6500.0, 0.707),
            gate_gain: 1.0,
            gate_is_open: false,
            gate_hold_counter: 0,
            vad_grace_counter: 0,
            de_ess_env: 0.0,
        }
    }

    pub fn pre_process(
        &mut self,
        samples: &mut [f32; FRAME_SAMPLES],
        rnn_in: &mut [f32; FRAME_SAMPLES],
        params: &DspParams,
    ) -> (f32, f32) {
        for s in samples.iter_mut() {
            *s = self.rumble_hpf.process(*s);
        }

        let mut in_peak = 0.0f32;
        let mut in_sum_sq = 0.0f32;
        for &val in samples.iter() {
            let abs = val.abs();
            if abs > in_peak {
                in_peak = abs;
            }
            in_sum_sq += val * val;
        }
        let in_rms = (in_sum_sq / FRAME_SAMPLES as f32).sqrt();
        let in_peak_db = if in_peak <= 0.000001f32 {
            -120.0f32
        } else {
            20.0f32 * in_peak.log10()
        };
        let in_rms_db = if in_rms <= 0.000001f32 {
            -120.0f32
        } else {
            20.0f32 * in_rms.log10()
        };

        self.low
            .configure_low_shelf(SAMPLE_RATE as f64, 180.0, params.low_eq_db as f64);
        self.mid
            .configure_peaking(SAMPLE_RATE as f64, 1200.0, 0.8, params.mid_eq_db as f64);
        self.high
            .configure_high_shelf(SAMPLE_RATE as f64, 6000.0, params.high_eq_db as f64);
        let linear_gain = 10.0f32.powf(params.gain_db / 20.0f32);

        for (i, s) in samples.iter_mut().enumerate() {
            let mut v = *s;
            v = self.low.process(v);
            v = self.mid.process(v);
            v = self.high.process(v);
            v *= linear_gain;
            *s = v;
            rnn_in[i] = v * 32767.0f32;
        }

        (in_peak_db, in_rms_db)
    }

    pub fn post_rnnoise(
        &mut self,
        samples: &mut [f32; FRAME_SAMPLES],
        rnn_out: &[f32; FRAME_SAMPLES],
        vad_prob: f32,
        params: &DspParams,
    ) {
        let vad = vad_prob.clamp(0.0, 1.0);
        let intensity = params.suppression_intensity.clamp(0.0, 1.0);

        for i in 0..FRAME_SAMPLES {
            let rnnoise_sample = rnn_out[i] / 32767.0f32;
            let orig = samples[i];
            samples[i] = orig * (1.0f32 - intensity) + rnnoise_sample * intensity;
        }

        let mut post_peak = 0.0f32;
        let mut post_sum_sq = 0.0f32;
        for &s in samples.iter() {
            let val = self.sidechain_hpf.process(s);
            let abs = val.abs();
            if abs > post_peak {
                post_peak = abs;
            }
            post_sum_sq += val * val;
        }
        let post_rms = (post_sum_sq / FRAME_SAMPLES as f32).sqrt();
        let post_peak_db = if post_peak <= 0.000001f32 {
            -120.0f32
        } else {
            20.0f32 * post_peak.log10()
        };
        let post_rms_db = if post_rms <= 0.000001f32 {
            -120.0f32
        } else {
            20.0f32 * post_rms.log10()
        };

        self.apply_intelligent_gate(samples, post_peak_db, post_rms_db, vad, params);
        self.post_process(samples, params);
    }

    pub fn fallback_process(
        &mut self,
        samples: &mut [f32; FRAME_SAMPLES],
        in_peak_db: f32,
        in_rms_db: f32,
        params: &DspParams,
    ) -> f32 {
        let voice_prob = if in_rms_db > params.gate_threshold_db + 4.0 {
            ((in_rms_db - (params.gate_threshold_db + 4.0)) / 18.0 + 0.4).clamp(0.0, 1.0)
        } else {
            0.0
        };

        let intensity = params.suppression_intensity.clamp(0.0, 1.0);
        if intensity > 0.05 {
            let noise_cut = 1.0 - (intensity * 0.45);
            let mult = if voice_prob > 0.35 { 1.0 } else { noise_cut };
            for s in samples.iter_mut() {
                *s *= mult;
            }
        }

        self.apply_intelligent_gate(samples, in_peak_db, in_rms_db, voice_prob, params);
        self.post_process(samples, params);
        voice_prob
    }

    fn apply_intelligent_gate(
        &mut self,
        frame: &mut [f32; FRAME_SAMPLES],
        peak_db: f32,
        rms_db: f32,
        vad_prob: f32,
        params: &DspParams,
    ) {
        let open_thresh = params.gate_threshold_db;
        let close_thresh = params.gate_threshold_db - params.gate_hysteresis_db.max(3.0);
        let hold_frames_total = (params.gate_hold_ms.max(10.0) / 10.0).ceil() as i32;

        let grace_frames = (params.vad_hold_ms.max(0.0) / 10.0).ceil() as i32;
        if vad_prob >= params.vad_threshold.clamp(0.05, 0.95) {
            self.vad_grace_counter = grace_frames;
        } else if self.vad_grace_counter > 0 {
            self.vad_grace_counter -= 1;
        }
        let in_grace = self.vad_grace_counter > 0;

        let above_open =
            peak_db >= open_thresh || rms_db >= (open_thresh - 6.0) || vad_prob >= 0.10 || in_grace;
        let above_close = peak_db >= close_thresh
            || rms_db >= (close_thresh - 6.0)
            || vad_prob >= 0.05
            || in_grace;

        if above_open {
            self.gate_is_open = true;
            self.gate_hold_counter = hold_frames_total;
        } else if self.gate_is_open {
            if above_close {
                self.gate_hold_counter = hold_frames_total;
            } else if self.gate_hold_counter > 0 {
                self.gate_hold_counter -= 1;
            } else {
                self.gate_is_open = false;
            }
        }

        let target_gain = if self.gate_is_open { 1.0f32 } else { 0.0f32 };
        let attack_coeff = 0.35f32;
        let release_coeff = (-10.0f32 / params.gate_release_ms.max(20.0)).exp();

        for s in frame.iter_mut() {
            if target_gain >= self.gate_gain {
                self.gate_gain += (target_gain - self.gate_gain) * attack_coeff;
            } else {
                self.gate_gain = target_gain + (self.gate_gain - target_gain) * release_coeff;
            }
            *s *= self.gate_gain;
        }
    }

    fn post_process(&mut self, frame: &mut [f32; FRAME_SAMPLES], params: &DspParams) {
        if params.deesser_enabled != 0 {
            for s in frame.iter_mut() {
                let mut x = *s;
                let sib = self.sibilance_hpf.process(x);
                let a = sib.abs();
                self.de_ess_env = if a > self.de_ess_env {
                    a
                } else {
                    self.de_ess_env * 0.995
                };
                if self.de_ess_env > DEESS_THRESHOLD {
                    let g = DEESS_THRESHOLD / self.de_ess_env;
                    x -= sib * (1.0 - g);
                }
                *s = x;
            }
        }

        if params.limiter_enabled != 0 {
            for s in frame.iter_mut() {
                *s = soft_limit(*s);
            }
        }
    }
}

#[inline]
pub fn soft_limit(x: f32) -> f32 {
    let a = x.abs();
    if a <= LIMITER_KNEE {
        return x;
    }
    let room = 1.0 - LIMITER_KNEE;
    let y = LIMITER_KNEE + room * ((a - LIMITER_KNEE) / room).tanh();
    if x < 0.0 {
        -y
    } else {
        y
    }
}
