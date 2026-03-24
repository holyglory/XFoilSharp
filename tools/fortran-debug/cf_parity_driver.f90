program cf_parity_driver
  use bl_common_kernels, only: &
    cfl_kernel, cft_kernel, reset_cft_debug, &
    last_fc_arg, last_fc, last_grt, last_gex, last_arg, last_thk_arg, last_thk, &
    last_grt_ratio, last_thk_sq, last_one_minus_thk_sq, last_scaled_thk_diff, &
    last_cfo, last_cf_hk_term1, last_cf_hk_term2, last_cf_hk_term3, &
    last_cf_numerator, last_cf_msq_scale, last_cf_msq_lead_core, last_cf_msq_tail
  implicit none

  integer :: case_count
  integer :: case_index
  integer :: ityp
  integer :: selected_branch
  real :: hk
  real :: rt
  real :: msq
  real :: hk_t
  real :: hk_d
  real :: hk_u
  real :: hk_ms
  real :: rt_t
  real :: rt_u
  real :: rt_ms
  real :: m_u
  real :: m_ms
  real :: rt_re
  real :: cf
  real :: cf_hk
  real :: cf_rt
  real :: cf_m
  real :: cf_t
  real :: cf_d
  real :: cf_u
  real :: cf_ms
  real :: cf_re
  read(*,*) case_count
  write(*,'(I8)') case_count

  do case_index = 1, case_count
    read(*,*) ityp, hk, rt, msq
    read(*,*) hk_t, hk_d, hk_u, hk_ms
    read(*,*) rt_t, rt_u, rt_ms
    read(*,*) m_u, m_ms, rt_re

    call reset_cft_debug()

    call compute_cf_chain(ityp, hk, rt, msq, hk_t, hk_d, hk_u, hk_ms, &
      rt_t, rt_u, rt_ms, m_u, m_ms, rt_re, &
      selected_branch, cf, cf_hk, cf_rt, cf_m, cf_t, cf_d, cf_u, cf_ms, cf_re)

    if (ityp .eq. 2) then
      write(*,'(A,1X,I0,1X,I0,19(1X,Z8.8))') 'DTERM', case_index, ityp, &
        transfer(last_fc_arg, 0), transfer(last_fc, 0), transfer(last_grt, 0), transfer(last_gex, 0), transfer(last_arg, 0), &
        transfer(last_thk_arg, 0), transfer(last_thk, 0), transfer(last_grt_ratio, 0), &
        transfer(last_thk_sq, 0), transfer(last_one_minus_thk_sq, 0), transfer(last_scaled_thk_diff, 0), &
        transfer(last_cfo, 0), transfer(last_cf_hk_term1, 0), transfer(last_cf_hk_term2, 0), transfer(last_cf_hk_term3, 0), &
        transfer(last_cf_numerator, 0), transfer(last_cf_msq_scale, 0), transfer(last_cf_msq_lead_core, 0), transfer(last_cf_msq_tail, 0)
    end if

    write(*,'(A,1X,I0,1X,I0,1X,I0,4(1X,Z8.8))') 'TERMS', case_index, ityp, selected_branch, &
      transfer(cf, 0), transfer(cf_hk, 0), transfer(cf_rt, 0), transfer(cf_m, 0)
    write(*,'(A,1X,I0,1X,I0,6(1X,Z8.8))') 'FINAL', case_index, ityp, &
      transfer(cf, 0), transfer(cf_t, 0), transfer(cf_d, 0), &
      transfer(cf_u, 0), transfer(cf_ms, 0), transfer(cf_re, 0)
  end do

contains

  subroutine compute_cf_chain(ityp, hk, rt, msq, hk_t, hk_d, hk_u, hk_ms, &
      rt_t, rt_u, rt_ms, m_u, m_ms, rt_re, &
      selected_branch, cf, cf_hk, cf_rt, cf_m, cf_t, cf_d, cf_u, cf_ms, cf_re)
    implicit none

    integer, intent(in) :: ityp
    integer, intent(out) :: selected_branch
    real, intent(in) :: hk
    real, intent(in) :: rt
    real, intent(in) :: msq
    real, intent(in) :: hk_t
    real, intent(in) :: hk_d
    real, intent(in) :: hk_u
    real, intent(in) :: hk_ms
    real, intent(in) :: rt_t
    real, intent(in) :: rt_u
    real, intent(in) :: rt_ms
    real, intent(in) :: m_u
    real, intent(in) :: m_ms
    real, intent(in) :: rt_re
    real, intent(out) :: cf
    real, intent(out) :: cf_hk
    real, intent(out) :: cf_rt
    real, intent(out) :: cf_m
    real, intent(out) :: cf_t
    real, intent(out) :: cf_d
    real, intent(out) :: cf_u
    real, intent(out) :: cf_ms
    real, intent(out) :: cf_re

    real :: cf_lam
    real :: cf_lam_hk
    real :: cf_lam_rt
    real :: cf_lam_m

    cf = 0.0
    cf_hk = 0.0
    cf_rt = 0.0
    cf_m = 0.0
    cf_t = 0.0
    cf_d = 0.0
    cf_u = 0.0
    cf_ms = 0.0
    cf_re = 0.0
    selected_branch = 0

    if (ityp .eq. 3) then
      return
    else if (ityp .eq. 1) then
      call cfl_kernel(hk, rt, msq, cf, cf_hk, cf_rt, cf_m)
      selected_branch = 1
    else
      call cft_kernel(hk, rt, msq, cf, cf_hk, cf_rt, cf_m)
      selected_branch = 2

      call cfl_kernel(hk, rt, msq, cf_lam, cf_lam_hk, cf_lam_rt, cf_lam_m)
      if (cf_lam .gt. cf) then
        cf = cf_lam
        cf_hk = cf_lam_hk
        cf_rt = cf_lam_rt
        cf_m = cf_lam_m
        selected_branch = 3
      end if
    end if

    cf_t = cf_hk*hk_t + cf_rt*rt_t
    cf_d = cf_hk*hk_d
    cf_u = cf_hk*hk_u + cf_rt*rt_u + cf_m*m_u
    cf_ms = cf_hk*hk_ms + cf_rt*rt_ms + cf_m*m_ms
    cf_re = cf_rt*rt_re
  end subroutine compute_cf_chain

end program cf_parity_driver
