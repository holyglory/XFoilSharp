module bl_common_kernels
  implicit none

  real :: last_fc_arg = 0.0
  real :: last_fc = 0.0
  real :: last_grt = 0.0
  real :: last_gex = 0.0
  real :: last_arg = 0.0
  real :: last_thk_arg = 0.0
  real :: last_thk = 0.0
  real :: last_grt_ratio = 0.0
  real :: last_thk_sq = 0.0
  real :: last_one_minus_thk_sq = 0.0
  real :: last_scaled_thk_diff = 0.0
  real :: last_cfo = 0.0
  real :: last_cf_hk_term1 = 0.0
  real :: last_cf_hk_term2 = 0.0
  real :: last_cf_hk_term3 = 0.0
  real :: last_cf_numerator = 0.0
  real :: last_cf_msq_scale = 0.0
  real :: last_cf_msq_lead_core = 0.0
  real :: last_cf_msq_tail = 0.0

contains

  subroutine reset_cft_debug()
    implicit none

    last_fc_arg = 0.0
    last_fc = 0.0
    last_grt = 0.0
    last_gex = 0.0
    last_arg = 0.0
    last_thk_arg = 0.0
    last_thk = 0.0
    last_grt_ratio = 0.0
    last_thk_sq = 0.0
    last_one_minus_thk_sq = 0.0
    last_scaled_thk_diff = 0.0
    last_cfo = 0.0
    last_cf_hk_term1 = 0.0
    last_cf_hk_term2 = 0.0
    last_cf_hk_term3 = 0.0
    last_cf_numerator = 0.0
    last_cf_msq_scale = 0.0
    last_cf_msq_lead_core = 0.0
    last_cf_msq_tail = 0.0
  end subroutine reset_cft_debug

  subroutine cfl_kernel(hk, rt, msq, cf, cf_hk, cf_rt, cf_msq)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: rt
    real, intent(in) :: msq
    real, intent(out) :: cf
    real, intent(out) :: cf_hk
    real, intent(out) :: cf_rt
    real, intent(out) :: cf_msq
    real :: tmp

    if (hk .lt. 5.5) then
      tmp = (5.5 - hk)**3 / (hk + 1.0)
      cf = (0.0727*tmp - 0.07) / rt
      cf_hk = (-0.0727*tmp*3.0/(5.5 - hk) - 0.0727*tmp/(hk + 1.0)) / rt
    else
      tmp = 1.0 - 1.0/(hk - 4.5)
      cf = (0.015*tmp**2 - 0.07) / rt
      cf_hk = (0.015*tmp*2.0/(hk - 4.5)**2) / rt
    end if

    cf_rt = -cf / rt
    cf_msq = 0.0
  end subroutine cfl_kernel

  subroutine cft_kernel(hk, rt, msq, cf, cf_hk, cf_rt, cf_msq)
    implicit none

    real, intent(in) :: hk
    real, intent(in) :: rt
    real, intent(in) :: msq
    real, intent(out) :: cf
    real, intent(out) :: cf_hk
    real, intent(out) :: cf_rt
    real, intent(out) :: cf_msq
    real :: gam
    real :: cffac
    real :: gm1

    gam = 1.4
    cffac = 1.0
    gm1 = gam - 1.0
    last_fc_arg = 1.0 + 0.5*gm1*msq
    last_fc = sqrt(last_fc_arg)
    last_grt = log(rt / last_fc)
    last_grt = max(last_grt, 3.0)
    last_gex = -1.74 - 0.31*hk
    last_arg = -1.33*hk
    last_arg = max(-20.0, last_arg)
    last_thk_arg = 4.0 - hk/0.875
    last_thk = tanh(last_thk_arg)
    last_grt_ratio = last_grt / 2.3026
    last_cfo = cffac * 0.3*exp(last_arg) * last_grt_ratio**last_gex

    last_cf_numerator = last_cfo + 1.1e-4*(last_thk - 1.0)
    cf = last_cf_numerator / last_fc
    last_cf_hk_term1 = -1.33*last_cfo
    last_cf_hk_term2 = -0.31*log(last_grt_ratio)*last_cfo
    last_thk_sq = last_thk**2
    last_one_minus_thk_sq = 1.0 - last_thk_sq
    last_scaled_thk_diff = -1.1e-4*last_one_minus_thk_sq
    last_cf_hk_term3 = last_scaled_thk_diff / 0.875
    cf_hk = (last_cf_hk_term1 + last_cf_hk_term2 + last_cf_hk_term3) / last_fc
    cf_rt = last_gex*last_cfo / (last_fc*last_grt) / rt
    last_cf_msq_scale = 0.25*gm1 / last_fc**2
    last_cf_msq_lead_core = last_gex*last_cfo / (last_fc*last_grt)
    last_cf_msq_tail = last_cf_msq_scale * cf
    cf_msq = -(last_cf_msq_lead_core * last_cf_msq_scale) - last_cf_msq_tail
  end subroutine cft_kernel

end module bl_common_kernels
