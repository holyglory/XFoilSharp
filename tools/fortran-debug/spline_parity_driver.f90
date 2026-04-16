program spline_parity_driver
  implicit none

  integer, parameter :: max_n = 64
  integer, parameter :: max_eval = 64
  integer :: case_count
  integer :: case_index
  integer :: point_count
  integer :: eval_count
  integer :: i
  integer :: ios
  real :: values(max_n)
  real :: derivatives(max_n)
  real :: parameters(max_n)
  real :: eval_points(max_eval)
  real :: start_bc
  real :: end_bc
  real :: eval_value
  real :: eval_derivative
  real :: seval
  real :: deval

  external :: seval
  external :: deval

  read(*, *, iostat = ios) case_count
  if (ios /= 0) then
    write(*, '(A)') 'ERROR: failed to read case count'
    stop 1
  end if

  write(*, '(I0)') case_count

  do case_index = 1, case_count
    read(*, *, iostat = ios) point_count, eval_count, start_bc, end_bc
    if (ios /= 0) then
      write(*, '(A,I0)') 'ERROR: failed to read case header ', case_index
      stop 2
    end if

    if (point_count < 2 .or. point_count > max_n) then
      write(*, '(A,I0)') 'ERROR: invalid point count ', point_count
      stop 3
    end if

    if (eval_count < 1 .or. eval_count > max_eval) then
      write(*, '(A,I0)') 'ERROR: invalid eval count ', eval_count
      stop 4
    end if

    do i = 1, point_count
      read(*, *, iostat = ios) parameters(i), values(i)
      if (ios /= 0) then
        write(*, '(A,I0,A,I0)') 'ERROR: failed to read point ', i, ' for case ', case_index
        stop 5
      end if
    end do

    do i = 1, eval_count
      read(*, *, iostat = ios) eval_points(i)
      if (ios /= 0) then
        write(*, '(A,I0,A,I0)') 'ERROR: failed to read eval point ', i, ' for case ', case_index
        stop 6
      end if
    end do

    call splind(values, derivatives, parameters, point_count, start_bc, end_bc)

    write(*, '(I0,1X,I0)') point_count, eval_count

    do i = 1, point_count
      write(*, '(Z8.8,1X,1PE24.16)') transfer(derivatives(i), 0), derivatives(i)
    end do

    do i = 1, eval_count
      eval_value = seval(eval_points(i), values, derivatives, parameters, point_count)
      eval_derivative = deval(eval_points(i), values, derivatives, parameters, point_count)
      write(*, '(Z8.8,1X,1PE24.16,1X,Z8.8,1X,1PE24.16)') &
        transfer(eval_value, 0), eval_value, transfer(eval_derivative, 0), eval_derivative
    end do
  end do
end program spline_parity_driver
