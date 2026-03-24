program pswlin_half_parity_driver
  implicit none

  integer :: case_count
  integer :: case_index

  read(*, *) case_count
  write(*, '(I8)') case_count

  do case_index = 1, case_count
    call run_case(case_index)
  end do

contains

  subroutine run_case(case_index)
    implicit none
    integer, intent(in) :: case_index
    real :: x1
    real :: x2
    real :: yy
    real :: x1i
    real :: x2i
    real :: yyi
    real :: x0
    real :: psum
    real :: pdif
    real :: psx0
    real :: psx1
    real :: psyy
    real :: pdyy
    real :: dsio
    real :: dsim
    real :: dxinv
    real :: qopi
    real :: half
    real :: two
    real :: pdx0term1
    real :: pdx0term2
    real :: pdx0term3
    real :: pdx0accum1
    real :: pdx0accum2
    real :: pdx0numerator
    real :: pdx0split
    real :: pdx0direct
    real :: pdx1term1
    real :: pdx1term2
    real :: pdx1term3
    real :: pdx1accum1
    real :: pdx1accum2
    real :: pdx1numerator
    real :: pdx1split
    real :: pdx1direct
    real :: pdyyterm2
    real(kind=8) :: pdyysumwide
    real :: psni
    real :: pdni
    real :: dqjoleft
    real :: dqjoright
    real :: dqjoinner
    real :: dqjo

    call read_hex_real(x1)
    call read_hex_real(x2)
    call read_hex_real(yy)
    call read_hex_real(x1i)
    call read_hex_real(x2i)
    call read_hex_real(yyi)
    call read_hex_real(x0)
    call read_hex_real(psum)
    call read_hex_real(pdif)
    call read_hex_real(psx0)
    call read_hex_real(psx1)
    call read_hex_real(psyy)
    call read_hex_real(dsio)
    call read_hex_real(dsim)
    call read_hex_real(dxinv)
    call read_hex_real(qopi)

    half = 0.5
    two = 2.0

    pdx0term1 = (x1 + x0) * psx0
    pdx0term2 = psum
    pdx0term3 = -two * x0 * psx0
    pdx0accum1 = pdx0term1 + pdx0term2
    pdx0accum2 = pdx0accum1 + pdx0term3
    pdx0numerator = pdx0accum2 + pdif
    pdx0split = pdx0numerator * dxinv
    pdx0direct = ((x1 + x0) * psx0 + psum + pdx0term3 + pdif) * dxinv

    pdx1term1 = (x1 + x0) * psx1
    pdx1term2 = psum
    pdx1term3 = -two * x1 * psx1
    pdx1accum1 = pdx1term1 + pdx1term2
    pdx1accum2 = pdx1accum1 + pdx1term3
    pdx1numerator = pdx1accum2 - pdif
    pdx1split = pdx1numerator * dxinv
    pdx1direct = ((x1 + x0) * psx1 + psum + pdx1term3 - pdif) * dxinv

    pdyyterm2 = two * (x0 - x1 - yy * (psx1 + psx0))
    pdyysumwide = dble(x1 + x0) * dble(psyy) + dble(pdyyterm2)
    pdyy = real(pdyysumwide) * dxinv
    psni = psx1 * x1i + psx0 * (x1i + x2i) * half + psyy * yyi
    pdni = pdx1direct * x1i + pdx0direct * (x1i + x2i) * half + pdyy * yyi
    dqjoleft = -psni * dsio
    dqjoright = pdni * dsio
    dqjoinner = dqjoleft - dqjoright
    dqjo = qopi * dqjoinner

    write(*, 1000) case_index, &
      transfer(x0, 0), transfer(psum, 0), transfer(pdif, 0), &
      transfer(psx0, 0), transfer(psx1, 0), transfer(psyy, 0), &
      transfer(pdx0term1, 0), transfer(pdx0term2, 0), transfer(pdx0term3, 0), &
      transfer(pdx0accum1, 0), transfer(pdx0accum2, 0), transfer(pdx0numerator, 0), &
      transfer(pdx0split, 0), transfer(pdx0direct, 0), &
      transfer(pdx1term1, 0), transfer(pdx1term2, 0), transfer(pdx1term3, 0), &
      transfer(pdx1accum1, 0), transfer(pdx1accum2, 0), transfer(pdx1numerator, 0), &
      transfer(pdx1split, 0), transfer(pdx1direct, 0), &
      transfer(pdyy, 0), transfer(psni, 0), transfer(pdni, 0), &
      transfer(dqjoleft, 0), transfer(dqjoright, 0), transfer(dqjoinner, 0), &
      transfer(dqjo, 0)

1000 format('CASE',1X,I0,29(1X,Z8.8))
  end subroutine run_case

  subroutine read_hex_real(value)
    implicit none
    real, intent(out) :: value
    integer :: bits

    read(*, '(Z8)') bits
    value = transfer(bits, value)
  end subroutine read_hex_real

end program pswlin_half_parity_driver
